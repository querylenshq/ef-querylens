# Provider Support

EF QueryLens targets mainstream EF Core providers with IDE-first SQL preview workflows.

| Provider | Status | Notes |
|---|---|---|
| MySQL | Supported | Validate provider-specific SQL syntax against Pomelo/MySQL runtime in your project. |
| PostgreSQL | Supported | Generated SQL follows PostgreSQL dialect behavior from your EF provider stack. |
| SQL Server | Supported | Generated SQL follows SQL Server provider conventions and parameterization patterns. |

## Notes

- SQL text is generated through your project's EF Core provider behavior.
- Provider-specific differences in SQL shape are expected.
- If SQL output appears unexpected, verify your project provider package versions first.
