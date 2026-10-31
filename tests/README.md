# Test strategy

- Domain unit tests: lifecycle invariants and validation.
- Application tests: use-case behavior with repository ports.
- Infrastructure integration tests: SQL Server in an isolated disposable database.
- API contract tests: request validation, authorization, and response compatibility.
- AI tests: deterministic contracts and evaluation fixtures; no live model calls in CI.
- Media tests: FFmpeg fixture-based output validation.

CI must not require production credentials or connect to live databases. Integration tests must use an explicitly provisioned disposable test database.
