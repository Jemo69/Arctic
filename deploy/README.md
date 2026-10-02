# deploy

What actually deploys this is [`azure/`](azure/README.md) — Bicep against
Azure Container Apps, two environments, two regions. See that README for how,
and
[`docs/architecture/why-container-apps.md`](../docs/architecture/why-container-apps.md)
for why that and not Kubernetes.

[`local/`](local) is `docker-compose` and the scripts around it, for running
the stack on a laptop.

`helm/` and `envs/` are empty, and nothing that deploys reads either one.
They are left over from before the Container Apps decision, when the plan was
Argo CD watching per-service Helm charts under `envs/` in this repository.
