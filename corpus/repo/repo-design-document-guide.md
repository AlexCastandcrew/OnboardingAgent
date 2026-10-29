# How to Write a Design Document

For anyone writing a design document before implementation. A design document is what the squad builds from and what a
decision maker approves. It states decisions. It is not a status report and not a record of the thinking.

Model to follow: [employee-payroll-import-design.md](../HTGPlus.EmployeeSetupCore/docs/employee-payroll-import-design.md).

---

## 1. Who reads it

Two readers, two parts.
- **A decision maker** (a director or executive) reads the **Summary** and stops. It must stand alone.
- **The implementers** read the numbered sections.

Write the Summary last, in plain language: no class names, no mechanics, every term defined on first use. It answers, in this
order:
1. **Why.** What is wrong now, in one short paragraph, in terms of the business or the user.
2. **The design.** Five or six bullets, one idea each.
3. **What it gives the business.**
4. **What we need from others.** Other teams' work (a contract change, a topic to provision). This is the part a decision maker
   can act on, so it is never left only in the body.
5. **Risks we accept.** In plain words, with who notices and how it recovers.
6. **Scope.**

## 2. What goes in

**The design is the target state.** Write what the system will do, in the present tense, as if it is built.
- A thing that does not exist yet is not a gap to flag. If the design needs it (a contract change, a new topic, a new
  field), it is a design element. If it must exist before the code can start, list it once as a prerequisite.
- Do not check whether a designed thing already exists in the code, and do not close a section with "not built yet" or
  "who builds it". That belongs in tickets.
- No branch names, no ticket numbers, no "how it works today" headings, no "already in code" rows. The one exception is
  the Problem section (section 5), and only when the design fixes a problem that exists: it says why the work is needed.
  It lists the behavior that causes the problem and its consequence, nothing else about how the current system works.

**Only decisions go in.** The document holds what was decided, not how we got there.
- No cases that cannot happen. No options we considered and rejected. No reasoning whose only purpose is to show we thought
  about it.
- Document every **material trade-off** of a decision: what we chose, and what it costs or gives up. Name the alternative
  only when the cost is visible only against it (for example one forwarder per domain costs one more consumer group and
  topic per domain, in exchange for isolated lag). A trade-off is material when a reviewer would need it to accept the
  decision or when someone will hit its cost later. Everything else stays out. When a decision has no material
  trade-off, write "Trade-offs: N/A". Do not invent one to fill the space.
- A deliberate choice is not a limitation. "No dead-letter topic" is a choice (nobody watches a topic; the log, a
  dashboard and a replay click do the job). Write what we do, not what we do not have.
- A real limit that we accept is written once, as an accepted limit: what happens, what it takes to happen, who notices, how
  it recovers.

**No open decisions.** The finished document has none.
- When something looks open, stop and ask the designer one question at a time. Resolve it, then write the decision.
- Do not write "TBD", "to be decided", "recommended" or a list of options in the document.

**Stay in scope.** If a scenario leads into a neighboring design (for example how a screen saves and deletes), keep it out of
this document and note it for its own design.

## 3. Weigh a mechanism against the problem it solves

Before adding a mechanism (a tombstone, a cache marker, a new field from another team, a second delivery), compare it with
the problem.
- **How often** can the problem happen, and what has to line up for it to happen?
- **How bad** is it, and who notices? A visible problem that the user fixes with one click is cheap.
- **What the mechanism costs,** including other teams. A new field that another team must store, return and never reuse is
  expensive. A new dependency adds a failure mode of its own.

If the problem is rare and cheap to recover from, and the mechanism is not, accept the limit and write it as an accepted
limit (section 2). Say so when a proposed fix costs more than it saves.

## 4. Ground it in industry practice

When the design uses a known pattern, say so, name it, and give references, so a reader knows it was researched and not
invented. Priority lanes on Kafka (a topic and consumer group per priority class) are an example.
- Cite only what the source supports. Do not attach a reference to a claim the source does not make.
- Open the sources before you cite them.
- Put the references at the end, and one sentence where the pattern is used.

## 5. Structure

0. **Summary** (section 1).
1. **Problem.** Why this work is needed, when the design fixes a problem that exists: the behavior that causes it, and the
   consequence of each. Only what you verified in the code. A new capability with no existing problem says "N/A".
2. **Goals and non-goals.**
3. **Architecture.** One diagram of the flow and one sentence per part saying its single job.
4. **One section per component.** What it does, what it does not do, the decisions.
5. **Failure modes.** One table: failure, what happens, recovery. Every way a message or request can be lost or stuck.
6. **Scale.** What limits throughput, what gives ordering, what is isolated from what.
7. **Observability.** What is logged, with which identifiers, what is never logged (personal data), which dashboards and
   alerts exist, how to verify after deploy.
8. **Decisions and prerequisites.** Prerequisites outside the code, stated once.
9. **Out of scope.**
10. **References.**

**A section that does not apply says "N/A".** The list above is a checklist of what to consider, not a form to fill in.
When a section has nothing real to say for this design, write the heading and "N/A", with a few words on why only if it is
not obvious. Do not fill it with filler, a restatement of another section, or something invented to make it look
complete. Examples: References when the design uses no known pattern; Out of scope when it only repeats the Summary's
Scope; Scale when nothing in the design changes throughput or ordering; Trade-offs when a decision costs nothing. A
reader should be able to see at a glance that the author considered the section and found nothing to put in it.

Keep build steps in a separate plan document. The design explains how the pieces fit and why. The plan lists what to
build in what order.

## 6. Keep it short

- Say each fact once. If a table already holds it (failure modes), do not list it again in prose or a second table.
- Do not explain how an existing system works beyond what the Problem section needs. Explain the new design.
- A design of this size (a forwarder, two lanes, retry, a guard, failure handling) is about 300 lines. After the first draft,
  cut: background, repeated lists, a summary of the plan, option tables whose outcome is one sentence.

## 7. Be accurate

- Every statement about the current system must be checked in the code. Do not infer it. Wrong statements about the old
  system were the most common mistake when this process was first used: a claim that one slow type blocked the others
  (the old service ran independent consumers), a claim that no replacement existed for a call (one did), a recovery row
  that contradicted a later decision.
- Name the real thing: the class, the topic, the field, the stored procedure. Say "the consumer" only if you mean one.
- After the document is done, re-read it top to bottom. Look for a sentence that contradicts a decision made later.

## 8. Test the design with scenarios

Walk each scenario through the design end to end, as a message moves: normal, retry, delete, out of order, two sources
for the same record, a pod crash in the middle. Writing the scenario down finds gaps that reading the design does not.
- **Start from the user's screen,** not from the code: what the user does, in what order, how fast. Ask what the screen and
  the other system can actually do at each step (for example a delete cannot be sent for a record that has no key yet).
- **Re-run the earlier scenarios after every fix.** A fix can move a gap instead of closing it. Putting deletes in one lane
  fixed one race and opened the opposite one.
- **Ask whether the case can happen at all** before designing for it.

Example: following a delete and an older update for the same record showed that the two had different ordering keys, so
the ordering guarantee did not hold. The fix was a rule on the key.

Check these every time:
- **Order.** What key decides order, and does every message that must be ordered share it?
- **Priority.** Who goes first, and what stops a bulk load from delaying a user?
- **Collisions.** Can two consumers write the same record at once, or can an old message overwrite a new one?
- **Delete.** What happens to a late save after a delete, and what stays behind (a soft-deleted row, junk rows)?
- **Retry.** What is retried, how many times, where does the message go back, what happens at the last attempt, and can
  it be replayed?
- **Personal data.** Is any personal data in a log line, a dead-letter record or a message that people can read?

## 9. Style

- Short and direct. Tables for facts. Each section states the design.
- One idea per bullet. No history, no apologies, no "we first thought".
- The designer's rules in the repo instructions apply to the design as they do to the code (for example no business logic
  in a consumer that only forwards, and no workaround for a gap).

## 10. Before you send it for review

- [ ] Summary at the top, in plain language, readable alone: why, design, business gain, what we need from others, risks, scope.
- [ ] Written as the target state, present tense.
- [ ] Only decisions and their material trade-offs. No immaterial options, no cases that cannot happen.
- [ ] No open decisions, no "TBD", no "recommended".
- [ ] No branch names or ticket numbers.
- [ ] Every statement about the current system checked in the code.
- [ ] Each mechanism weighed against the problem; accepted limits written once.
- [ ] Industry pattern named, with references that were opened and say what the document claims.
- [ ] Failure modes, scale and observability are covered, each fact once.
- [ ] Every section that does not apply says "N/A". None is filled with filler or a repeat of another section.
- [ ] Scenarios walked through from the user's screen, including delete, retry and out of order, and re-run after each fix.
- [ ] No personal data in logs.
- [ ] Re-read once for contradictions, then cut to the shortest version.
- [ ] Build steps are in the plan, not in the design.
