# AI Context Guide

Use this folder to avoid loading the long project documents for every task.

## What to Read

| Task | Read |
|---|---|
| Any task | `/AGENTS.md` + this file |
| Scope, user flow, recommendation types, ERP boundaries | `PROJECT.md` |
| Services, transactions, workers, security | `ARCHITECTURE.md` |
| Exact entities, SQL schema, indexes, constraints, migrations, seed | `DATABASE.md` |
| Events, APIs, error semantics, ERP field mapping | `DATA_AND_CONTRACTS.md` |
| Task order, ownership, current priorities | `WORK_PLAN.md` |
| Writing a good agent request | `PROMPT_GUIDE.md` |

Most tasks need `AGENTS.md` plus only one compact file.

For ERP schema or contract work, read both:

- `DATABASE.md`
- `DATA_AND_CONTRACTS.md`

## Full Documents

The detailed source documents remain references for exact details:

- `docs/project-plan.md`
- `docs/technical-architecture.md`
- `docs/database-design.md`
- `docs/software-structure.md`
- `docs/task-distribution-and-work-plan.md`
- `docs/erp.docx` — ERP screen/field comparison and gap review

Open a full source document only when the compact files do not contain a
required detail.

## ERP Scope Decision

The ERP comparison identified several real-ERP fields. The AI files include
only the fields required for this project:

Included:

- payment method in ERP order transfer,
- immutable ERP order delivery address,
- stock unit, net content, and reorder level,
- direct accounting customer reference,
- accounting voucher type, date, description, and account-coded lines.

Deliberately excluded:

- tax/identity number,
- open-account balance,
- supplier type,
- draft/approval/cancellation workflow,
- dispatch note,
- multi-warehouse/location,
- legal accounting and e-document compliance.

## Conflict Rule

Use this priority:

1. Approved new decision or ADR
2. `DATABASE.md` for schema and persistence
3. `AGENTS.md` and the remaining `docs/ai/*`
4. `docs/api-contracts.md`
5. Current EF Core migrations
6. Detailed source documents
7. Existing code

When two sources conflict, report the conflict and fix both code and
documentation. Do not silently guess.
