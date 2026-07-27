# AI Context Guide

Use this folder to avoid loading the five long project documents for every task.

## What to Read

| Task | Read |
|---|---|
| Any task | `/AGENTS.md` + this file |
| Scope, user flow, recommendation types | `PROJECT.md` |
| Services, transactions, workers, security | `ARCHITECTURE.md` |
| Entities, database rules, events, APIs | `DATA_AND_CONTRACTS.md` |
| Task order, ownership, current priorities | `WORK_PLAN.md` |
| Writing a good agent request | `PROMPT_GUIDE.md` |

Most tasks need `AGENTS.md` plus only one compact file.

## Full Documents

The detailed Turkish documents remain authoritative for exact details:

- `docs/project-plan.md`
- `docs/technical-architecture.md`
- `docs/database-design.md`
- `docs/software-structure.md`
- `docs/task-distribution-and-work-plan.md`

Open a full document only when the compact files do not contain a required detail.

## Conflict Rule

Use this priority:

1. Approved new decision or ADR
2. `AGENTS.md` and `docs/ai/*`
3. `docs/api-contracts.md`
4. Current EF Core migrations
5. Detailed Turkish documents
6. Existing code

When two sources conflict, report the conflict and fix both code and documentation. Do not silently guess.
