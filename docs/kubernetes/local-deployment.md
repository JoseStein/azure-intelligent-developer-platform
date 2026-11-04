# Local Docker and Kubernetes deployment

## What was built

The API uses a multi-stage .NET 10 Docker build:

1. SDK stage restores, builds, and publishes `src/Aidp.Api/Aidp.Api.csproj`.
2. ASP.NET runtime stage runs `dotnet Aidp.Api.dll` on port `8080`.
3. Knowledge files under `docs/knowledge` are copied into the image.

The image was built locally as `aidp-api:local` and ran successfully with `ASPNETCORE_ENVIRONMENT=Development`. `GET /health` returned HTTP 200.

## Kubernetes resources

The local manifests create:

- Namespace: `aidp-dev`
- Deployment: `aidp-api`, one replica
- Service: internal `ClusterIP`, port 80 to container port 8080
- ConfigMap: `aidp-api-config`

The Deployment uses `/health` for readiness and liveness, requests `100m` CPU/`128Mi` memory, and limits usage to `500m` CPU/`512Mi` memory.

## Verification workflow

```bash
kubectl apply -f k8s/local/namespace.yaml
kubectl apply -f k8s/local/configmap.yaml
kubectl apply -f k8s/local/deployment.yaml
kubectl apply -f k8s/local/service.yaml
kubectl rollout status deployment/aidp-api -n aidp-dev
kubectl get pods,services,endpoints -n aidp-dev
kubectl port-forward -n aidp-dev service/aidp-api 8082:80
curl -i http://localhost:8082/health
```

Port-forwarding was used only for local validation; it is not a production exposure pattern.

## Hands-on Kubernetes skills demonstrated

The exercises covered namespaces, Services and endpoints, ConfigMaps, resource requests/limits, readiness/liveness probes, scaling, rolling updates, rollback, image-pull failures, readiness failures, and CrashLoopBackOff diagnosis.
