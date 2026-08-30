# Security Policy

## Scope

Planizer is an offline static analyzer: it reads SQL files from paths you pass on the command
line and writes a report to stdout or a file. It does not connect to a database, does not send
telemetry and makes no network calls at analysis time. The composite GitHub Action builds the
CLI from this repository's source and runs it inside your own workflow.

The main classes of issue we consider security-relevant:

- Malicious SQL input causing anything beyond an analysis finding (crashes are bugs; code
  execution, path traversal via report output paths, or resource exhaustion are security issues).
- Supply-chain concerns in the published artifacts (release binaries, Docker image, the action).

## Reporting a vulnerability

Please **do not open a public issue** for suspected vulnerabilities. Use GitHub's private
vulnerability reporting instead: **Security → Report a vulnerability** on this repository.
You will get a response within a week. Fixes are released as a patch version, and the report is
credited unless you prefer otherwise.
