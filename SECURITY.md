# Security and privacy

Do not post credentials, personal machine configuration, user paths, hardware identifiers, full logs, ETW traces or crash dumps in public issues. Redact and minimize reproductions first.

No public security-reporting address has been configured. Do not submit secrets to an invented contact. If a secret is exposed, revoke it with its issuer; deleting the latest file or commit does not revoke credentials or remove old Git objects.

Before publishing changes, run `python scripts/audit-public.py .` and inspect every finding, including history and metadata. The scanner is a heuristic check, not a guarantee. Enable the hosting provider's secret scanning when available. Do not upload private backups, compiled releases or the contents of runtime Data.
