# H2G Frontend Standards

### React 19 | MobX | MUI

> **Version**: 1.1 | **Status**: Active | **Owner**: All the Team

> This is the single source of truth for how we write frontend code in H2G.
> Propose changes via Pull Request. Approved changes take effect immediately.

**See also:** [FIX_VERSION_CONVENTION.md](FIX_VERSION_CONVENTION.md) · [ARCHITECTURE_MIGRATION.md](ARCHITECTURE_MIGRATION.md) · [MODULE_INVENTORY.md](MODULE_INVENTORY.md)

---

## Table of Contents

1. [Folder Structure](#1-folder-structure)
2. [Naming Conventions](#2-naming-conventions)
3. [Component Rules](#3-component-rules)
4. [MobX Store Conventions](#4-mobx-store-conventions) → [Full reference: MOBX_CONVENTIONS.md](MOBX_CONVENTIONS.md)
5. [MUI & Styling Conventions](#5-mui--styling-conventions)
6. [SOLID Principles in Practice](#6-solid-principles-in-practice)
7. [Clean Code Rules](#7-clean-code-rules)
8. [Immutability & Pure Functions](#8-immutability--pure-functions)
9. [Architecture: Lazy Loading & Module Isolation](#9-architecture-lazy-loading--module-isolation)
10. [Pre-Commit & Tooling](#10-pre-commit--tooling)
11. [Component Unit Testing](#11-component-unit-testing) _(to be defined)_

---

## 1. Folder Structure

> **Superseded (2026-09):** the folder layout, barrel and cross-feature rules below are replaced by
> `HTGPlus.Web.UI/CLAUDE.md` §1 — `app/core` → `app/routes` → `app/features/<domain>` (`index.ts`) →
> `app/shared`, features may import each other only through `index.ts`, applied by the big-bang
> migration (HTG-6974; see the [Web.UI developer guide](../HTGPlus.Web.UI/README.md)). Kept here for history until the Handbook is rewritten.

### 1.1 Target Structure (Shield Frontend Pattern)

This is the structure every new module must follow. Existing modules migrate toward it incrementally — one sprint at a time.

**Shield Pattern rules:**

1. Every feature is self-contained under `app/modules/[name]/`
2. Each module exposes exactly **one public entry point**: `index.js`
3. Nothing inside a module is imported directly from outside — only through `index.js`
4. Shared/cross-module code lives exclusively in `app/shared/` — never inside a module
5. API calls live inside the module that owns them (`modules/[name]/services/`)

```
app/
+-- modules/
|   +-- employees/
|   |   +-- index.js            # Public API — only exports EmployeesPage + useEmployees
|   |   +-- components/         # UI components used only by this module
|   |   +-- store/              # One store per logical domain (max ~400 lines each)
|   |   +-- services/           # API calls — replaces app/api/employees/
|   |   +-- hooks/
|   |   +-- utils/
|   |   +-- __tests__/          # Tests for this module only — components, store, utils
|   |
|   +-- dealmemo/
|   |   +-- index.js
|   |   +-- components/
|   |   +-- store/
|   |   +-- services/
|   |   +-- hooks/
|   |   +-- __tests__/          # Tests for this module only — components, store, utils
|   |
|   +-- [other modules follow the same shape]
|
+-- shared/                     # Used by 3+ modules only — never feature-specific
|   +-- components/             # Truly reusable UI: DateInput, AutoComplete, Table, etc.
|   +-- hooks/
|   +-- utils/
|   +-- constants/
|   +-- __tests__/              # Tests for this module only — components, store, utils
|
+-- stores/
|   +-- rootStore.js            # Thin coordinator only — no god stores
|
+-- routing/
|   +-- AppRouter.jsx
|   +-- AppHistory.js
|
+-- services/                   # App-level infrastructure only (auth, ajax, session)
|
+-- theme/                      # MUI theme configuration
|   +-- index.js
|
+-- assets/
```

### 1.2 When to Extract a Module

| Condition                                               | Action                                                                |
| ------------------------------------------------------- | --------------------------------------------------------------------- |
| New feature with 1–2 components, no store, no API calls | Place in `shared/components/` temporarily                             |
| New feature has its own store **or** makes API calls    | Create `modules/[name]/` immediately                                  |
| Existing component reaches 200 lines                    | Extract to its own file; if it has logic, extract to a module         |
| Store reaches 400 lines                                 | Split — each logical domain gets its own store inside its module      |
| Component is used by 3+ modules                         | Move to `shared/components/`                                          |
| God store (1,500+ lines)                                | Break into per-domain stores inside their respective module           |
| Folder name starts with `new*`                          | Rename + restructure to full Shield module before adding any new code |

#### Import Rule

```js
// ✅ CORRECT — import only from module's public index
import { EmployeesPage } from '@/modules/employees';
import { DashboardPage } from '@/modules/dashboard';

// ❌ WRONG — never reach inside a module's internals
import { EmployeeTable } from '@/modules/employees/components/EmployeeTable';
import { employeeStore } from '@/modules/employees/store/employeeStore';
```

#### Module Dependency Rule

- Modules must not import from other modules.
- Modules can depend only on `shared/` or base layers (`utility/`, `services/`, `constants/`).
- If two modules need the same code, move it to `shared/`.

```js
// ✅ CORRECT — modules depend on shared only
import { CurrencyInput } from '@/shared/components';
import { formatDate } from '@/utility/date';

// ❌ WRONG — cross-module dependency breaks Shield boundaries
import { usePayroll } from '@/modules/prodco';
import { EmployeeSearch } from '@/modules/employees';
```

#### Barrel File Template

Every `index.js` must follow this structure:

```js
// modules/employees/index.js
// Public API for the Employees module.
// Only export what other modules are explicitly allowed to consume.

export { EmployeesPage } from './components/EmployeesPage';
export { useEmployees } from './hooks/useEmployees';

// DO NOT export: internal components, store internals, raw service functions
```

---

## 2. Naming Conventions

> **File names superseded:** components `PascalCase.tsx`, pages `XxxPage.tsx`, store files
> `xxxStore.ts` (class `XxxStore`), API modules `xxx.api.ts`, hooks `useXxx.ts`, no `new*` prefixes —
> see `HTGPlus.Web.UI/CLAUDE.md` §3. The identifier conventions below still apply.

| What                | Convention                 | Example                               |
| ------------------- | -------------------------- | ------------------------------------- |
| React components    | PascalCase                 | `EmployeeCard`, `PayrollTable`        |
| Component files     | PascalCase                 | `EmployeeCard.jsx`                    |
| Non-component files | camelCase                  | `usePayroll.js`, `payrollService.js`  |
| MobX stores         | PascalCase + `Store`       | `PayrollStore`, `EmployeeStore`       |
| Store files         | PascalCase + `Store`       | `PayrollStore.js`                     |
| Constants           | `SCREAMING_SNAKE_CASE`     | `MAX_HOURS_PER_WEEK`                  |
| Custom hooks        | `use` prefix + camelCase   | `usePayrollData`, `useEmployeeFilter` |
| Services            | camelCase + `Service`      | `payrollService`, `employeeService`   |
| Utility functions   | camelCase, verb-first      | `calculateGross()`, `formatHours()`   |
| Boolean variables   | `is/has/can/should` prefix | `isLoading`, `hasError`, `canEdit`    |

### Names must be self-explanatory

```js
// ❌ Bad
const d = employees.filter(e => e.s === 'active');
const x = d.map(e => calcG(e));

// ✅ Good
const activeEmployees = employees.filter(e => e.status === 'active');
const grossAmounts = activeEmployees.map(employee => calculateGross(employee));
```

---

## 3. Component Rules

### Rule 1: One component = one job

If you need to scroll to read a component, it does too much. Split it.

```jsx
// ❌ Bad — fetches, transforms, and renders
function PayrollPage() {
  const [data, setData] = useState([]);
  useEffect(() => {
    fetch('/api/payroll')
      .then(r => r.json())
      .then(setData);
  }, []);
  const rows = data.map(d => ({ ...d, gross: d.hours * d.rate }));
  return <Table rows={rows} />;
}

// ✅ Good — each layer does one job
// Container (reads store, connects data)
const PayrollContainer = observer(() => {
  useEffect(() => {
    payrollStore.fetchEntries();
  }, []);
  return <PayrollTable rows={payrollStore.rows} />;
});

// Component (renders only)
function PayrollTable({ rows }) {
  return <Table rows={rows} />;
}
```

### Rule 2: Component size limits

| Limit            | Guideline                                 |
| ---------------- | ----------------------------------------- |
| Lines            | Aim for < 100. Hard limit: 200.           |
| Props            | Aim for < 5. If more, consider splitting. |
| useEffect calls  | Max 2. If more, extract a custom hook.    |
| Responsibilities | Exactly 1.                                |

### Rule 3: Containers vs Components

- **Components** (`components/`) are dumb. They receive props. They emit events via callbacks. They never import stores directly.
- **Containers** (`containers/`) are smart. They import stores, pass data as props to components.

```jsx
// ✅ Container — talks to store
const EmployeeListContainer = observer(() => (
  <EmployeeList
    employees={employeeStore.filteredEmployees}
    onSelect={employeeStore.selectEmployee}
    isLoading={employeeStore.isLoading}
  />
));

// ✅ Component — pure rendering
function EmployeeList({ employees, onSelect, isLoading }) {
  if (isLoading) return <CircularProgress />;
  return employees.map(emp => (
    <EmployeeCard key={emp.id} employee={emp} onClick={() => onSelect(emp.id)} />
  ));
}
```

### Rule 4: No business logic in JSX

```jsx
// ❌ Bad — logic inside JSX
return (
  <Box>
    {employees
      .filter(e => e.hours > 40)
      .map(e => (
        <Card key={e.id}>{(e.hours - 40) * e.rate * 1.5 + 40 * e.rate}</Card>
      ))}
  </Box>
);

// ✅ Good — logic extracted
const overtimeEmployees = employees.filter(hasOvertime);
return (
  <Box>
    {overtimeEmployees.map(e => (
      <Card key={e.id}>{calculateTotalPay(e)}</Card>
    ))}
  </Box>
);
```

---

## 4. MobX Store Conventions

> Full reference: **[MOBX_CONVENTIONS.md](MOBX_CONVENTIONS.md)**

The key rules at a glance:

- Store structure is always: **State → Computed → Actions → Export**
- Call `makeAutoObservable(this)` in the constructor — no exceptions
- Wrap every post-`await` mutation in `runInAction()` — async boundaries exit the action context
- Use `get` computed properties for derived state — never store what can be calculated
- UI state (`isModalOpen`, `activeTab`) belongs in `useState`, not in MobX
- One store per domain; split when a store exceeds ~400 lines
- Stores within a module may import each other — stores across modules may not

---

## 5. MUI & Styling Conventions

### Rule 1: Always use the theme

```jsx
// ❌ Never hardcode visual values
<Box sx={{ color: '#1A4A8C', padding: '16px', fontSize: '14px', fontWeight: 700 }}>

// ✅ Use theme tokens
<Box sx={{ color: 'primary.main', p: 2, typography: 'body2' }}>
```

### Rule 2: All overrides go in `app/theme/`

Never override MUI components inline across files. One place, one source of truth.

```js
// app/theme/index.js
export const theme = createTheme({
  palette: {
    primary: { main: '#1A4A8C' },
  },
  components: {
    MuiButton: {
      styleOverrides: {
        root: { borderRadius: 8 },
      },
    },
  },
});
```

### Rule 3: Composition over customization

```jsx
// ❌ Bad — wraps with arbitrary divs + inline styles
<div style={{ display: 'flex', gap: 8 }}>
  <Button style={{ backgroundColor: 'red' }}>Delete</Button>
</div>

// ✅ Good — uses MUI's system
<Stack direction="row" spacing={1}>
  <Button variant="outlined" color="error">Delete</Button>
  <Button variant="contained">Save</Button>
</Stack>
```

### Rule 4: No legacy patterns

```
❌ makeStyles()      — MUI v4, do not use
❌ withStyles()      — MUI v4, do not use
❌ styled-components — not in our stack
✅ sx prop           — preferred for one-off overrides using theme tokens
✅ theme.components  — preferred for global component overrides
```

---

## 6. SOLID Principles in Practice

| Letter | Principle             | What it means in H2G                                                                                                        |
| ------ | --------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| **S**  | Single Responsibility | One component = one job. One store = one domain. One function = one operation. If you add "and" to a description, split it. |
| **O**  | Open/Closed           | Extend components via props. Do not modify a shared component to add a feature — add a prop instead.                        |
| **L**  | Liskov Substitution   | If a child component replaces a parent, it must still satisfy the same contract (same required props, same behavior).       |
| **I**  | Interface Segregation | Props should be small. Do not pass a large object when only two fields are needed.                                          |
| **D**  | Dependency Inversion  | Components depend on props (abstractions), not on specific store instances. Pass store data as props via containers.        |

```jsx
// ❌ Violates D — component directly imports and reads store
function EmployeeCard() {
  const name = employeeStore.selected.name;
  return <div>{name}</div>;
}

// ✅ Follows D — component depends on props
function EmployeeCard({ name }) {
  return <div>{name}</div>;
}
// Container handles the store dependency
const EmployeeCardContainer = observer(() => <EmployeeCard name={employeeStore.selected.name} />);
```

---

## 7. Clean Code Rules

### Names must not need comments

```js
// ❌ Bad — what does this mean?
const d = new Date();
const r = e.filter(x => x.s > d);

// ✅ Good — self-documenting
const today = new Date();
const futureEvents = events.filter(event => event.startDate > today);
```

### Functions must be short

- Target: under 20 lines
- Hard limit: 40 lines
- If a function needs a comment to explain what it does, rename it or split it

### No magic numbers

```js
// ❌ Bad
if (employee.hours > 40) { ... }
const overtime = (employee.hours - 40) * employee.rate * 1.5;

// ✅ Good
const REGULAR_HOURS_LIMIT = 40;
const OVERTIME_MULTIPLIER = 1.5;

if (employee.hours > REGULAR_HOURS_LIMIT) { ... }
const overtimePay = (employee.hours - REGULAR_HOURS_LIMIT) * employee.rate * OVERTIME_MULTIPLIER;
```

### Delete dead code

```js
// ❌ Never leave commented-out code
// const oldFunction = () => { ... }
// function legacyCalculation() { ... }

// ✅ Delete it. Git history preserves it.
```

### Comments explain WHY, not WHAT

```js
// ❌ Useless — the code already says this
// Loop through employees
employees.forEach(employee => { ... });

// ✅ Useful — explains a non-obvious decision
// MUI DataGrid requires stable row IDs to prevent re-render flicker.
// Using composite key because employee.id alone is not unique across periods.
const rowId = `${employee.id}-${periodId}`;
```

### The "simple and maintainable" rule

> A simple solution that a junior developer can understand and modify tomorrow is always better than a clever solution that only you understand today.

```js
// ❌ "Smart" — no one else can maintain this
const result = data.reduce(
  (acc, { id, v, s }) => ({
    ...acc,
    [id]: acc[id]
      ? { ...acc[id], total: acc[id].total + v, [s]: (acc[id][s] || 0) + 1 }
      : { total: v, [s]: 1 },
  }),
  {}
);

// ✅ Simple — any developer can read, debug, and extend this
const result = {};
for (const item of data) {
  if (!result[item.id]) {
    result[item.id] = { total: 0 };
  }
  result[item.id].total += item.value;
  result[item.id][item.status] = (result[item.id][item.status] ?? 0) + 1;
}
```

---

## 8. Immutability & Pure Functions

### Never mutate what you do not own

```js
// ❌ Bad — mutates the original object
function applyBonus(employee, amount) {
  employee.salary += amount; // mutates the argument
}

// ✅ Good — returns a new object
function applyBonus(employee, amount) {
  return { ...employee, salary: employee.salary + amount };
}

// ❌ Bad — mutates array
function addEntry(entries, newEntry) {
  entries.push(newEntry);
}

// ✅ Good — returns new array
function addEntry(entries, newEntry) {
  return [...entries, newEntry];
}
```

### Pure functions — the rule

A pure function:

1. Always returns the same output for the same input
2. Has no side effects (no API calls, no store mutations, no console.log)

Put pure functions in `utils/`. They are easy to test and impossible to misuse.

```js
// ✅ Pure — predictable, testable, no side effects
export function calculateGross(hours, rate) {
  const REGULAR_LIMIT = 40;
  const OVERTIME_RATE = 1.5;
  if (hours <= REGULAR_LIMIT) return hours * rate;
  return REGULAR_LIMIT * rate + (hours - REGULAR_LIMIT) * rate * OVERTIME_RATE;
}
```

---

## 9. Architecture: Lazy Loading & Module Isolation

> **Superseded (2026-09):** the gradual plan below was replaced by a big-bang migration
> (HTG-6974); lazy routes will live in `app/routes/` (Phase 4, `HTGPlus.Web.UI/docs/followup/LAZY_LOADING_SPEC.md`).

### The target: isolated modules with lazy loading

```
Before (monolith):
App --> imports everything --> one bundle --> everything loads at startup

After (lazy-loaded modules):
App --> React.lazy() per route --> separate bundle per module --> loads on demand
```

```jsx
// app/routing/AppRouter.jsx
import { lazy, Suspense } from 'react';
import { Routes, Route } from 'react-router-dom';

// Each module is a separate bundle — loaded only when navigated to
const PayrollModule = lazy(() => import('modules/payroll'));
const EmployeeModule = lazy(() => import('modules/employee'));
const HoursModule = lazy(() => import('modules/hours'));

export function AppRouter() {
  return (
    <Suspense fallback={<PageLoader />}>
      <Routes>
        <Route path="/payroll/*" element={<PayrollModule />} />
        <Route path="/employee/*" element={<EmployeeModule />} />
        <Route path="/hours/*" element={<HoursModule />} />
      </Routes>
    </Suspense>
  );
}
```

### Each module exposes one entry point

```js
// modules/payroll/index.js
export default function PayrollModule() {
  return (
    <Routes>
      <Route path="/" element={<PayrollListContainer />} />
      <Route path="/:id" element={<PayrollDetailContainer />} />
    </Routes>
  );
}

// Public API for other modules
export { payrollStore } from './stores/PayrollStore';
```

### State isolation rule

```
✅ Module store is created inside the module
✅ Module store is exported via index.js for other modules that need it
❌ Never import another module's store directly — use the public API
❌ Never share a store between modules by reference — pass data as props or via events
```

### Migration plan (gradual, not big-bang)

| Sprint         | Action                                                                    |
| -------------- | ------------------------------------------------------------------------- |
| Sprint 1 (now) | Add `index.js` public API to all existing modules. No logic change.       |
| Sprint 2–3     | Wrap module routes in `React.lazy()`. Verify bundle split in network tab. |
| Sprint 4–6     | Migrate stores to live inside modules, not in a global `/stores` folder.  |
| Sprint 7+      | Enforce module boundary rule via ESLint `import/no-internal-modules`.     |

---

## 10. Pre-Commit & Tooling

### The tools and what they do

| Tool         | What it does                                | When it runs                   |
| ------------ | ------------------------------------------- | ------------------------------ |
| ESLint       | Catches code quality issues                 | On save (VS Code) + pre-commit |
| Prettier     | Formats code automatically                  | On save (VS Code) + pre-commit |
| EditorConfig | Enforces indentation, line endings          | In your editor, always         |
| Husky        | Runs scripts on git events                  | On `git commit`                |
| lint-staged  | Runs ESLint + Prettier only on staged files | Via Husky                      |

### If the pre-commit hook blocks your commit

```bash
# Read the error — it tells you exactly what is wrong
# Fix the issue, then stage the fixed file:
git add <fixed-file>
git commit -m "your message"

# ❌ Never bypass the hook with:
git commit --no-verify   # This is a code smell. Fix the issue.
```

### VS Code setup (required extensions)

Install the extensions listed in `.vscode/extensions.json`. They activate:

- Format on save (Prettier)
- ESLint inline error highlighting

---

## 11. Component Unit Testing

> **Status: To be defined.**
> This section will cover conventions for writing unit and integration tests for React components.
> Topics to address when ready:
>
> - Chosen test runner and assertion library (e.g. Vitest + Testing Library)
> - What to test in components vs. stores vs. utils
> - How to handle MobX stores in tests (real instance vs. stub)
> - File naming convention: `__tests__/` folder inside each module (never a global test folder — Shield boundary rule applies to tests too)
> - Coverage thresholds per module type
> - CI integration

---

_End of FRONTEND_STANDARDS.md — last updated: see git history_
