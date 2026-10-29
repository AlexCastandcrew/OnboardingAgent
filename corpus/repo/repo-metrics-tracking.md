# H2G Frontend — Metrics Tracking

> Fill in the **Baseline** column after running the Cursor diagnostic.
> Update **Week 4**, **Week 8**, **Week 12** as the initiative progresses.
> The goal is a visible downward trend in violations and upward trend in health scores.

---

## Health Scorecard

| Dimension                   | Baseline | Week 4 | Week 8 | Week 12 | Target |
| --------------------------- | -------- | ------ | ------ | ------- | ------ |
| Project structure (0–10)    | —        |        |        |         | 8      |
| Component health (0–10)     | —        |        |        |         | 8      |
| MobX correctness (0–10)     | —        |        |        |         | 9      |
| Type safety (0–10)          | —        |        |        |         | 9      |
| Code quality signals (0–10) | —        |        |        |         | 8      |
| Naming conventions (0–10)   | —        |        |        |         | 9      |
| Module boundaries (0–10)    | —        |        |        |         | 8      |
| MUI usage (0–10)            | —        |        |        |         | 8      |
| Tooling coverage (0–10)     | —        |        |        |         | 10     |

---

## Violation Counts (lower = better)

| Metric                            | Baseline | Week 4 | Week 8 | Week 12 | Target |
| --------------------------------- | -------- | ------ | ------ | ------- | ------ |
| `any` type usages                 | —        |        |        |         | 0      |
| `console.log` statements          | —        |        |        |         | 0      |
| Hardcoded hex colors              | —        |        |        |         | 0      |
| Components > 200 lines            | —        |        |        |         | 0      |
| API calls inside components       | —        |        |        |         | 0      |
| Cross-module direct imports       | —        |        |        |         | 0      |
| MobX async without `runInAction`  | —        |        |        |         | 0      |
| Missing `index.ts` at module root | —        |        |        |         | 0      |
| TODO/FIXME comments               | —        |        |        |         | < 5    |

---

## PR Quality Metrics (auto-reported by GitHub Actions)

| Metric                         | Baseline | Week 4 | Week 8 | Week 12 | Target |
| ------------------------------ | -------- | ------ | ------ | ------- | ------ |
| Avg PR review time (hours)     | —        |        |        |         | < 4h   |
| PRs with ESLint errors at open | —        |        |        |         | 0%     |
| PRs with correct branch name   | —        |        |        |         | 100%   |
| PRs using the template         | —        |        |        |         | 100%   |
| PRs with localhost evidence    | —        |        |        |         | 100%   |

---

## Sprint Bug Rate

| Sprint          | Bug count | Bugs related to state mutation | Bugs related to wrong env |
| --------------- | --------- | ------------------------------ | ------------------------- |
| Baseline sprint | —         | —                              | —                         |
| Sprint +4       |           |                                |                           |
| Sprint +8       |           |                                |                           |

---

## How metrics are collected

- **Violation counts**: Run Cursor diagnostic prompt monthly, or use `npm run lint -- --format json` and count errors.
- **PR metrics**: Automatically posted as a comment on every PR by `.github/workflows/pr-quality-check.yml`.
- **Bug rate**: Manually tracked from Jira — filter by bug type and sprint.
- **Health scores**: Tech Lead judgment call based on Cursor diagnostic output.

---

## Review cadence

| Review            | Frequency       | Owner         | Where             |
| ----------------- | --------------- | ------------- | ----------------- |
| Metrics update    | Every 2 sprints | Tech Lead     | This file, via PR |
| Team health check | Monthly         | Eng Manager   | Slack + meeting   |
| Handbook updates  | As needed       | Any developer | PR to this repo   |
