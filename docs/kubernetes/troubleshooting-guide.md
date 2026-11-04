# AIDP Kubernetes Troubleshooting Guide

These exercises were performed locally on Docker Desktop Kubernetes. The original failure simulations ran before the namespace migration in `default`; reusable current commands use `-n aidp-dev`.

This guide records the local Docker Desktop Kubernetes failures practiced with the AIDP API. The exercises were intentionally triggered and then recovered. They were performed before the namespace migration, when the workload ran in `default`; reusable commands below target the current namespace, `aidp-dev`.

> **Scope:** local learning only. Do not run the intentional failure commands in a shared or production cluster.

## Baseline

The API runs as Deployment `aidp-api` using the local image `aidp-api:local`. The Service is a `ClusterIP` on port 80 and forwards to container port 8080. The `/health` endpoint is used for readiness and liveness probes.

Current reusable checks:

```bash
kubectl get deployment,service,pods,endpoints -n aidp-dev
kubectl rollout status deployment/aidp-api -n aidp-dev
kubectl logs -n aidp-dev deployment/aidp-api
```

During the original exercises, the resources were in `default`. The migration later recreated them in `aidp-dev`; do not assume both namespaces contain active copies now.

## 1. ImagePullBackOff

### Symptoms

The live Deployment image was intentionally changed to `aidp-api:does-not-exist`:

```bash
# LOCAL LAB ONLY — intentionally requests a nonexistent image
kubectl set image deployment/aidp-api aidp-api=aidp-api:does-not-exist
kubectl rollout status deployment/aidp-api --timeout=30s
```

The rollout timed out. The replacement Pod was observed as `ErrImagePull`, followed by `ImagePullBackOff`.

Observed event message:

```text
Failed to pull image "aidp-api:does-not-exist": Error response from daemon: pull access denied for aidp-api, repository does not exist or may require 'docker login'
```

The Pod could not start its container, so `kubectl logs` reported that the container was still waiting to pull the image.

### Concepts

- `ErrImagePull` means the node could not obtain the image.
- `ImagePullBackOff` means Kubernetes is retrying with increasing delays.
- A Deployment can keep old replicas while a replacement is not ready.
- A Service includes only ready endpoints.

### Troubleshooting sequence

```bash
kubectl get pods -n aidp-dev -o wide
kubectl describe pod -n aidp-dev <failing-pod>
kubectl get events -n aidp-dev --sort-by=.metadata.creationTimestamp
kubectl logs -n aidp-dev <failing-pod>
kubectl get endpoints aidp-api -n aidp-dev
```

`kubectl describe pod` and Events are the authoritative first checks for image-pull failures. Application logs are often unavailable because the application process never started.

### Impact and recovery

Before namespace migration, the two healthy old Pods remained in the `default` Service endpoints:

```text
10.1.0.10:8080,10.1.0.11:8080
```

The failed replacement was not included. Recovery used:

```bash
kubectl rollout undo deployment/aidp-api -n aidp-dev
kubectl rollout status deployment/aidp-api -n aidp-dev
```

The original image `aidp-api:local` was restored and the health endpoint returned HTTP 200 after port-forwarding.

### Practical lessons

Check the exact image name and tag, the cluster's image source, `imagePullPolicy`, and registry authentication before investigating application code.

## 2. Readiness probe failure

### Symptoms

Only the live Deployment was patched so the new Pod checked `/does-not-exist` instead of `/health`:

```bash
# LOCAL LAB ONLY — changes the live Deployment only
kubectl patch deployment aidp-api --type='strategic' -p='{"spec":{"template":{"spec":{"containers":[{"name":"aidp-api","readinessProbe":{"httpGet":{"path":"/does-not-exist","port":8080,"scheme":"HTTP"},"initialDelaySeconds":5,"periodSeconds":10,"timeoutSeconds":1,"failureThreshold":3,"successThreshold":1}}]}}}}'
```

The replacement Pod was `Running` but `0/1 Ready`. Events reported:

```text
Readiness probe failed: HTTP probe failed with statuscode: 404
```

The application logs looked normal: ASP.NET started and listened on port 8080. This proved the process was healthy enough to run; Kubernetes was simply checking the wrong path.

### Concepts

- `Running` describes the container process state.
- `Ready` means the Pod is eligible for Service traffic.
- Readiness failure does not normally restart the container.
- Unready Pods are removed from Service endpoints.

### Troubleshooting sequence

```bash
kubectl get pods -n aidp-dev -o wide
kubectl describe pod -n aidp-dev <new-pod>
kubectl logs -n aidp-dev <new-pod>
kubectl get endpoints aidp-api -n aidp-dev
```

Inspect the probe path, port, scheme, timing, and event messages. Compare the probe URL with a known application endpoint.

### Impact and recovery

The two old healthy Pods remained in the Service endpoints while the new Pod was unready. Recovery used:

```bash
kubectl rollout undo deployment/aidp-api -n aidp-dev
kubectl rollout status deployment/aidp-api -n aidp-dev
```

The probe returned to `/health`, all replicas became ready, and the Service health check returned HTTP 200.

### Practical lessons

Use a cheap, stable health endpoint. A readiness probe protects users from receiving traffic before the application is ready, but it does not prove every dependency is healthy.

## 3. CrashLoopBackOff

### Symptoms

The live container command was overridden to exit immediately:

```bash
# LOCAL LAB ONLY — forces the container process to exit
kubectl patch deployment aidp-api --type='strategic' -p='{"spec":{"template":{"spec":{"containers":[{"name":"aidp-api","command":["sh","-c","exit 1"]}]}}}}'
```

The replacement Pod entered `CrashLoopBackOff`. The observed container state was:

```text
State:        Terminated
Reason:       Error
Exit Code:    1
Restart Count: 2
```

Events showed repeated starts followed by `Back-off restarting failed container`.

### Concepts

- The image was available and the process started, unlike `ImagePullBackOff`.
- The process exited with code 1, so the kubelet restarted it.
- Backoff slows repeated restart attempts.

### Troubleshooting sequence

```bash
kubectl get pods -n aidp-dev
kubectl describe pod -n aidp-dev <failing-pod>
kubectl logs -n aidp-dev <failing-pod>
kubectl logs -n aidp-dev <failing-pod> --previous
kubectl get events -n aidp-dev --sort-by=.metadata.creationTimestamp
kubectl get endpoints aidp-api -n aidp-dev
```

`--previous` is especially useful when the current container has already restarted. In this exercise the command exited before producing output, so both current and previous logs were empty; `describe` and Events provided the root cause.

### Impact and recovery

The healthy old Pods stayed in the Service endpoints, so the Service continued to have available backends. Recovery used:

```bash
kubectl rollout undo deployment/aidp-api -n aidp-dev
kubectl rollout status deployment/aidp-api -n aidp-dev
```

The original image entrypoint was restored, both replicas became ready, and `/health` returned HTTP 200.

### Practical lessons

Check the command/args, exit code, environment, mounted files, and `--previous` logs. A restart loop is usually a startup or process-lifecycle problem, not a Service-routing problem.

## 4. Rolling updates and rollback

### Exercise

The Deployment initially used `aidp-api:local`. A second local tag was created without rebuilding:

```bash
docker tag aidp-api:local aidp-api:v2
kubectl set image deployment/aidp-api aidp-api=aidp-api:v2
kubectl rollout status deployment/aidp-api
```

The Deployment was scaled to two replicas for the exercise. Kubernetes created a new ReplicaSet and replaced the old Pods through its rolling-update strategy. The Service selector stayed the same and followed ready Pods as their revisions changed.

Useful diagnostics:

```bash
kubectl get deployment aidp-api -n aidp-dev -o yaml
kubectl get pods -n aidp-dev -o wide
kubectl get endpoints aidp-api -n aidp-dev
kubectl rollout history deployment/aidp-api -n aidp-dev
```

The exact rollout output and final Pod IPs are not repeated here because they depend on the local cluster state at the time of the exercise. Treat those commands as the reusable diagnostic procedure.

### Rollback

The previous revision was restored with:

```bash
kubectl rollout undo deployment/aidp-api -n aidp-dev
kubectl rollout status deployment/aidp-api -n aidp-dev
```

Rollback created/replaced Pods rather than changing a running container in place. The Service continued selecting ready Pods. A port-forward can disconnect when selected Pods are replaced; restart it when needed:

```bash
kubectl port-forward -n aidp-dev service/aidp-api 8082:80
curl -i http://localhost:8082/health
```

### Practical lessons

Use immutable, meaningful image tags and always validate rollout status, Pod readiness, endpoints, logs, and a functional health check. Roll back when a new revision is unhealthy or behavior is clearly worse, then investigate the failed revision separately.

## Quick-reference command cheat sheet

```bash
# Context and namespace state
kubectl config current-context
kubectl get all -n aidp-dev

# Deployment and rollout
kubectl get deployment aidp-api -n aidp-dev
kubectl describe deployment aidp-api -n aidp-dev
kubectl rollout status deployment/aidp-api -n aidp-dev
kubectl rollout history deployment/aidp-api -n aidp-dev
kubectl rollout undo deployment/aidp-api -n aidp-dev

# Pod diagnosis
kubectl get pods -n aidp-dev -o wide
kubectl describe pod -n aidp-dev <pod-name>
kubectl logs -n aidp-dev <pod-name>
kubectl logs -n aidp-dev <pod-name> --previous

# Events, Services, and endpoints
kubectl get events -n aidp-dev --sort-by=.metadata.creationTimestamp
kubectl get service aidp-api -n aidp-dev
kubectl get endpoints aidp-api -n aidp-dev

# Local functional test
kubectl port-forward -n aidp-dev service/aidp-api 8082:80
curl -i http://localhost:8082/health
```

## Interview-preparation summary

- **ImagePullBackOff:** the node cannot obtain the image; check image name/tag, registry access, and Pod Events.
- **Readiness failure:** the process may be running, but the Pod is not eligible for traffic; check the probe path, port, and endpoint response.
- **CrashLoopBackOff:** the process starts and exits repeatedly; check exit code, command/args, current logs, previous logs, and Events.
- **Rolling update:** Kubernetes creates a new ReplicaSet and gradually replaces old Pods while preserving ready capacity.
- **Rollback:** restore a known-good revision, then validate readiness, endpoints, logs, and a real request.
- **Service endpoints:** Services route only to selected, ready Pods; endpoint inspection shows what can receive traffic.
- **Namespace migration:** recreate and validate resources in the target namespace before deleting the old copies.

## LEARNING EXPLANATION

This document records real failure drills rather than generic theory. Incident notes matter because they turn one debugging session into a repeatable operating procedure for the team.

Use the guide by starting with the symptom, checking `describe` and Events, comparing Running versus Ready, inspecting logs, checking Service endpoints, and only then choosing a recovery action.

Remember these core concepts: images must be obtainable, processes must stay alive, readiness controls traffic eligibility, Services route to ready endpoints, Deployments manage replicas and revisions, and rollback restores a known-good desired state.
