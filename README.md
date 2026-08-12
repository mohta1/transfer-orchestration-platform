# transfer-orchestration-platform
# Resilient Interbank Transfer Orchestration Platform

Backend Engineering Challenge implementation for a resilient domestic
interbank transfer workflow.

## Status

Work in progress.

## Local runtime

See [docs/runtime-setup.md](docs/runtime-setup.md) for Docker Compose, database migrations, health checks, and clean build/test gates.

## Main objectives

- Domain-driven transfer model
- Safe balance reservation
- HTTP idempotency
- Concurrency protection
- Transactional Outbox
- Durable background processing
- Idempotent message consumption
- Incremental legacy modernisation
