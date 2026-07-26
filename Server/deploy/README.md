# CloSim Master Server — Deploy (Terraform + AWS App Runner)

This directory provisions the AWS infrastructure for the CloSim master server:
a stateless Node.js/Express HTTP directory API, run as a container on **AWS App
Runner** pulling its image from **ECR**, with the client API key stored in
**AWS Secrets Manager**.

## What this provisions

- **ECR repository** for the container image (or references an existing one).
- **Secrets Manager secret** `<project>/client-api-keys` for `CLIENT_API_KEYS`
  (value blank — you set it out-of-band).
- **App Runner service** pulling the ECR image, injecting all runtime env vars,
  health-checking `GET /healthz`, with autoscaling. App Runner supplies managed
  HTTPS and a public URL.
- **IAM roles**: an access role (App Runner pulls from ECR) and an instance role
  (the running service reads the secret at runtime).
- Optional **custom domain** association.

> Alternative architecture (see comments in `main.tf`): ECS Fargate + ALB. Not
> used here — App Runner is the simplest fit for a single stateless HTTP service.

## Prerequisites

- [Terraform](https://developer.hashicorp.com/terraform/downloads) >= 1.5
- [AWS CLI](https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html)
  v2, authenticated (`aws configure` / `AWS_PROFILE` / assumed role)
- [Docker](https://docs.docker.com/get-docker/) (to build and push the image)
- An AWS account with permission to create ECR, App Runner, IAM, and Secrets
  Manager resources.

## Fill in the blanks

Copy the example tfvars and fill every `# TODO: user provides`:

```bash
cp terraform.tfvars.example terraform.tfvars
```

Values you must supply:

- `aws_region` — e.g. `us-east-1`
- `ecr_image_uri` — full ECR image URI incl. tag (known after the repo exists)
- `region_label` — free-form region tag (optional but recommended)
- `custom_domain` — optional
- **`CLIENT_API_KEYS`** — the actual client API key(s). **Do not** put the real
  value in `terraform.tfvars`; set it directly in Secrets Manager (step 4).

`terraform.tfvars` is gitignored — the real secret and account-specific values
never get committed.

## Deploy sequence

### 1. Create the ECR repo (and IAM/secret scaffolding)

Leave `ecr_image_uri` blank for now, then:

```bash
terraform init
terraform apply -target=aws_ecr_repository.this
```

Grab the repo URL:

```bash
terraform output -raw ecr_repository_url
```

### 2. Build and push the image

From the `Server/` directory (the Dockerfile lives at `Server/Dockerfile`,
build context is `Server/`):

```bash
# Authenticate Docker to ECR (substitute your region + account id):
aws ecr get-login-password --region <aws_region> \
  | docker login --username AWS --password-stdin <account_id>.dkr.ecr.<aws_region>.amazonaws.com

# Build + tag + push (use the ECR repo URL from step 1):
docker build -t <ecr_repository_url>:latest .
docker push <ecr_repository_url>:latest
```

Then set `ecr_image_uri = "<ecr_repository_url>:latest"` in `terraform.tfvars`.

### 3. Apply the full stack

```bash
terraform plan
terraform apply
```

### 4. Set the client API key (out-of-band)

The secret is created with a blank placeholder; set the real value once:

```bash
aws secretsmanager put-secret-value \
  --secret-id "$(terraform output -raw secret_arn)" \
  --secret-string "your-key-1,your-key-2"
```

App Runner reads the secret at runtime, so no redeploy of infrastructure is
needed for a key rotation — trigger a new App Runner deployment (or update the
image) to pick up a changed secret value.

### 5. Get the app URL

```bash
terraform output -raw service_url
```

That HTTPS URL is what the game client points at. Verify:

```bash
curl "$(terraform output -raw service_url)/healthz"
```

## Outputs

- `service_url` — public HTTPS URL of the App Runner service
- `ecr_repository_url` — where to push the image
- `secret_arn` — the CLIENT_API_KEYS secret to populate
- `service_arn`, `custom_domain`

## Teardown

```bash
terraform destroy
```
