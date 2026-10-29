# Architecture Migration Guide

### From: coupled monolith → To: isolated modules with lazy loading

> **Status (2026-09): superseded.** The gradual plan below was replaced by a big-bang architecture +
> TypeScript migration (HTG-6974, closed after Phase 3 on 2026-10-01). Current structure and daily
> workflow: [Web.UI developer guide](../HTGPlus.Web.UI/README.md); binding rules: `HTGPlus.Web.UI/CLAUDE.md`;
> lazy loading (Phase 4): `HTGPlus.Web.UI/docs/followup/LAZY_LOADING_SPEC.md`. `app/modules/` is now
> `app/features/`, barrels are `index.ts`. The lazy-loading rationale below still holds.

---

## The problem we are solving

Currently the H2G frontend loads everything at startup. All modules, all stores, all components —
bundled into one file. This means:

- A developer working on the Payroll module can accidentally break the Hours module
- The initial page load is slow because unrelated code loads eagerly
- Store state from one module leaks into another
- It's impossible to split teams across modules safely

---

## The target: module isolation with lazy loading

```
Before                          After
──────────────────────────      ──────────────────────────────────────
App                             App
└── imports everything          ├── lazy(() => import('modules/payroll'))
    └── one bundle              ├── lazy(() => import('modules/employee'))
        └── loads at startup    └── lazy(() => import('modules/hours'))
                                    ↓
                                Each module = separate bundle
                                Loaded only when its route is visited
                                State is isolated inside the module
```

---

## How to build a new module (the correct pattern)

### 1. Folder structure

```
modules/
└── payroll/
    ├── components/
    │   ├── PayrollTable.tsx
    │   └── PayrollRow.tsx
    ├── containers/
    │   └── PayrollTableContainer.tsx
    ├── stores/
    │   └── PayrollStore.ts
    ├── services/
    │   └── payrollService.ts
    ├── hooks/
    │   └── usePayrollFilter.ts
    ├── types/
    │   └── index.ts
    ├── utils/
    │   └── calculateGross.ts
    ├── constants/
    │   └── index.ts
    ├── PayrollModule.tsx   ← module entry point (the default export)
    └── index.ts            ← public API (named exports only)
```

### 2. The module entry point

```tsx
// modules/payroll/PayrollModule.tsx
import { Routes, Route } from 'react-router-dom';
import { PayrollListContainer } from './containers/PayrollListContainer';
import { PayrollDetailContainer } from './containers/PayrollDetailContainer';

// This is what React.lazy() loads.
// It owns the module's routes.
export default function PayrollModule() {
  return (
    <Routes>
      <Route path="/" element={<PayrollListContainer />} />
      <Route path="/:id" element={<PayrollDetailContainer />} />
    </Routes>
  );
}
```

### 3. The public API (index.ts)

```ts
// modules/payroll/index.ts
// ONLY export what other modules legitimately need.
// Everything else is private to this module.

export { default } from './PayrollModule'; // the lazy-loaded entry
export { payrollStore } from './stores/PayrollStore'; // if other modules need it
export type { IPayrollEntry } from './types'; // shared types
```

### 4. Registering in the router

```tsx
// app/router.tsx
import { lazy, Suspense } from 'react';
import { Routes, Route } from 'react-router-dom';
import { PageLoader } from 'shared/components/PageLoader';

// Each of these becomes a separate JS bundle
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

### 5. How to verify the bundle split is working

Open the browser DevTools → Network tab → reload the page.
You should see the main bundle load, but NOT the individual module bundles.
Navigate to `/payroll` — now the payroll chunk should appear in the Network tab.
Navigate to `/employee` — the employee chunk appears, but payroll does not reload.

This confirms lazy loading is working correctly.

---

## Migration plan for existing modules

### Sprint 1 (immediate, no code changes)

For every existing module folder, add an `index.ts` that exports only the public API.
This is a pure organization change — no logic changes.

```bash
# For each module, create index.ts if it doesn't exist
# Only export what other modules currently import from it
```

### Sprint 2–3

Wrap each module's routes in `React.lazy()` in the router.
Test in the browser network tab that bundles split correctly.
Fix any dynamic import issues (circular deps, missing exports).

### Sprint 4–6

Migrate stores from a global `/stores` folder into their respective modules.
Verify no other module was importing those stores directly.
Update cross-module imports to use the module's `index.ts`.

### Sprint 7+

Enable the ESLint rule `import/no-internal-modules` to enforce boundaries automatically.
Any new violation will fail the pre-commit hook.

---

## State isolation rules

```
✅ Each module creates and owns its stores
✅ Stores exported from index.ts are accessible by other modules via public API
✅ Shared state that belongs to no module lives in shared/ with explicit ownership
❌ Never import from another module's store directly (use index.ts)
❌ Never pass store references through props — pass data and callbacks
❌ Never put application-wide state in a single god store
```

---

## Common questions

**Q: What if two modules need the same data?**
A: Either the data belongs to one module and the other uses its public API, or the data belongs in `shared/`. If you're unsure, ask the Tech Lead.

**Q: What if a module is too small to justify this structure?**
A: Use the full structure anyway. The cost of structure is low; the cost of spaghetti grows exponentially.

**Q: Do we need to migrate all existing modules before starting new features?**
A: No. New modules follow the pattern immediately. Existing modules migrate gradually. The boundary is: don't add new violations; fix old ones when you're in the area.

**Q: What is the Shield Frontend pattern referenced in the ticket?**
A: It's a broader concept where each module is treated as a mini-application with its own boundaries, public API, and isolated state — similar to micro-frontends but without the deployment complexity. What we're implementing here is the H2G interpretation of that philosophy.
