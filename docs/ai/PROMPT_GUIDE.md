# Prompt Guide for Coding Agents

## Best Prompt Structure

A good prompt contains six parts:

```text
1. Context
2. Task
3. Scope
4. Constraints
5. Acceptance criteria
6. Verification
```

Use one focused task per prompt.

Always include the Jira/task ID when available.

## Base Prompt

```text
Read AGENTS.md and docs/ai/README.md.
Read only the compact document relevant to this task.

Task ID: <JIRA-ID>
Goal: <one clear result>

Affected application/module:
- <application>
- <module>

Scope:
- <files or feature area allowed to change>
- <files or areas that must not change>

Constraints:
- Follow the architecture and data ownership rules.
- Do not add new frameworks or project references.
- Do not change contracts or schema unless explicitly requested.
- Keep controllers thin.
- Add or update tests.

Acceptance criteria:
- <observable behaviour 1>
- <observable behaviour 2>
- <test requirement>

Before editing:
1. List the files you will change.
2. Explain schema or contract impact.
3. Show the implementation plan.
4. Wait for approval.

After editing:
- Run the relevant tests.
- Run solution build.
- Summarise changed files and verification results.
```

## Implementation Prompt Example

```text
Read AGENTS.md, docs/ai/README.md, and docs/ai/DATA_AND_CONTRACTS.md.

Task ID: DB-01
Goal: Implement the Product entity and its EF Core configuration.

Application: OnlineMarket.Web
Module: Catalog

Scope:
- Product.cs
- ProductConfiguration.cs
- related UnitType enum if missing
- unit tests for domain validation
Do not create a migration yet.

Constraints:
- Match the approved Products schema.
- Price is VAT-exclusive.
- NetContent is not money.
- Do not add Generic Repository.
- Do not change other entities.

Acceptance criteria:
- Required fields and validation match the schema.
- Unique and check constraints are configured.
- Project builds and relevant tests pass.

Show the plan and file list before editing.
```

## Bug-Fix Prompt Example

```text
Read AGENTS.md and the compact file relevant to the failing area.

Problem:
<paste the exact error and the command that produced it>

Expected behaviour:
<what should happen>

Current behaviour:
<what happens instead>

Task:
1. Identify the root cause.
2. Explain it before changing code.
3. Make the smallest safe fix.
4. Add a regression test when possible.
5. Run the failing command again and report the result.

Do not refactor unrelated code.
```

## Review Prompt Example

```text
Read AGENTS.md and docs/ai/README.md.

Review the current branch against main.
Do not edit files.

Focus on:
- architecture boundaries,
- transaction safety,
- data ownership,
- security and authorisation,
- idempotency and retry,
- missing tests,
- schema/contract/document mismatch.

Return findings ordered by severity.
For each finding include:
- file and location,
- why it is a problem,
- expected fix,
- whether it blocks merge.
```

## Test-Writing Prompt Example

```text
Read AGENTS.md and the compact architecture/data document for this feature.

Task ID: <ID>
Goal: Add tests for <feature>.

Required scenarios:
- happy path,
- validation failure,
- authorisation/ownership failure,
- transaction rollback or concurrency when relevant,
- idempotent replay when relevant.

Use unit tests for pure rules.
Use real SQL Server integration tests for constraints, transactions, and concurrency.
Do not change production behaviour unless a test exposes a confirmed bug.
```

## Contract Prompt Example

```text
Read AGENTS.md and docs/ai/DATA_AND_CONTRACTS.md.

Task ID: <ID>
Goal: Add or update <endpoint/event>.

Before editing:
- list producer and consumer files,
- classify the change as compatible or breaking,
- define HTTP status, application error code, and retryability.

Rules:
- use V2 for breaking changes,
- keep producer and consumer DTOs separate,
- update docs/api-contracts.md,
- add serialization and ingestion tests,
- do not create a Shared.Contracts project.
```

## Prompting Rules

Do:

- ask for a plan before code,
- name the application and module,
- limit allowed files,
- give measurable acceptance criteria,
- provide exact errors and commands,
- ask for tests and verification,
- use a second agent to review important changes.

Avoid:

- “build the whole project,”
- “make it better,”
- “fix everything,”
- giving several unrelated tasks together,
- pasting secrets or real customer data,
- allowing an agent to redesign architecture without approval,
- running two agents in the same working tree at the same time.

## Recommended Working Pattern

```text
Agent 1: plan and implement one task
Human: inspect diff and run project
Agent 2: review only
Human: approve and commit
```

Use separate branches or Git worktrees for parallel agents.

## ERP Parity Prompt Example

```text
Read AGENTS.md, docs/ai/PROJECT.md, docs/ai/ARCHITECTURE.md,
docs/ai/DATA_AND_CONTRACTS.md, and docs/ai/DATABASE.md.

Task ID: ERP-PARITY
Goal: implement or review the approved ERP field-to-model parity changes.

Required scope:
- OrderReadyForErpV1 PaymentMethod
- IntegrationOrderSnapshot PaymentMethod
- ERP order delivery-address snapshot
- ERP stock UnitType, NetContent, and ReorderLevel
- accounting voucher header with direct ErpCustomerId
- accounting voucher lines with account codes 120, 600, and 391

Before editing:
1. Compare contracts, entities, EF configurations, migrations, and tests.
2. List every producer and consumer affected.
3. Identify whether the change is breaking for existing V1 JSON.
4. List the exact migration operations.
5. Wait for approval.

Rules:
- Do not add tax/identity number, open balance, supplier type, dispatch,
  multi-warehouse, or legal accounting features.
- Do not store sensitive payment data.
- PaymentMethod is a non-sensitive enum snapshot.
- CreateOrder persists order, lines, address snapshot, and payment method
  atomically with its idempotency record.
- CreateAccountingEntry persists header, lines, and idempotency record
  atomically.
- The accounting lines are:
  120 debit GrandTotal,
  600 credit Subtotal,
  391 credit VatTotal.
- Total debit must equal total credit.
- Keep Mock ERP explicitly described as a project simulation.

Verification:
- serialization/contract tests,
- migration-based SQL Server tests,
- idempotency tests,
- accounting-balance tests,
- full solution build and tests.
```

