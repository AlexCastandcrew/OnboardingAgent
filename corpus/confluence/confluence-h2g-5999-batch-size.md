# H2G-5999 — Why Removing Batch Size Targets the Wrong Problem

Source: Confluence page 5412388902, space HPS, last modified jul 25, 2026.

## Summary

[H2G-5999](https://castandcrew.atlassian.net/browse/H2G-5999) removes `ConsumerPollBatchSize` from the multiconsumer to reduce topic starvation in ExcelMapping. The intent is correct — users should not wait for one file to finish before the next begins. However, the fix targets the symptom (batch size) rather than the root cause (wrong consumer architecture). **The batch size removal should be rolled back.** The correct fix is to replace the multiconsumer with dedicated consumers, one per topic.

**Action required:** Roll back the removal of `ConsumerPollBatchSize` in the multiconsumer. The batch size provides a real (if small) efficiency gain and its removal does not solve the starvation problem.

## What H2G-5999 Tried to Fix

The multiconsumer subscribes to multiple topics and round-robins between them. With `ConsumerPollBatchSize = 5` and two topics A and B:

- The consumer polls 5 messages from topic A, processes all 5, then moves to topic B.
- If each message takes 10 seconds, topic B waits **50 seconds** before being read.
- Users uploading two files simultaneously see one file stuck at Queued while the other processes.

H2G-5999 removes the batch size so the consumer reads one message at a time before switching topics — reducing the starvation window from 50 seconds to 10 seconds per message.

## Why This Does Not Actually Fix the Problem

The multiconsumer has **one consumer thread** serving all topics. No matter how small the batch size is, a slow message on topic A still blocks topic B for the full duration of that message's processing.

- Batch size = 5 → topic B waits up to 50 seconds.
- Batch size = 1 (H2G-5999 fix) → topic B waits up to 10 seconds.
- Batch size = 1, message takes 2 minutes (large file, slow AI call) → topic B waits 2 minutes.

The starvation is not caused by the batch size. It is caused by **sequential processing on a single thread**. Reducing the batch size makes the problem less visible for small messages but does not eliminate it.

## Why Removing Batch Size Hurts Efficiency

Each Kafka poll is a network round-trip to the broker. With a batch size of N, you make roughly `total_messages / N` poll calls. With batch size = 1, you make one poll call per message — the maximum possible number of round-trips.

- The time saved per avoided poll call is small (1–5 ms per round-trip).
- But the number of extra poll calls across thousands of file imports adds unnecessary broker load with no processing benefit.
- The batch size was providing a real, if small, efficiency gain. Removing it takes that away for no meaningful improvement to the user experience.

## When Multiconsumer Is the Right Tool

The multiconsumer is appropriate for **low-volume topics where no user is actively waiting**. Examples:

- Reference data updates (producer, client, scaleBasisSelection) — arrive as trickles when an admin makes a change.
- Background sync topics — processing delay of seconds or minutes is invisible to users.

In these cases, consolidating multiple topics under one consumer saves pod overhead and the round-robin delay is irrelevant.

## Why ExcelMapping Is Not a Candidate for Multiconsumer

ExcelMapping processes user-initiated file uploads. Users are **actively watching the File Tracking screen** waiting for their file to move from Queued → InProgress → SentToCaps. Any delay is visible and frustrating.

- Each file can have multiple worksheets, each requiring template resolution, extraction, and full mapping pipeline execution.
- Processing time per file ranges from seconds (small Traditional file) to minutes (large AI/DocAI file).
- Two users uploading simultaneously must both see their files begin processing immediately — not one waiting for the other.

This is the same category as the rule engine and timecard importers: **high-priority, user-facing, delay is non-negotiable.** The multiconsumer architecture is fundamentally the wrong fit.

## The Root Cause and the Right Fix

The root cause is that the multiconsumer uses a single consumer loop for all topics. The correct fix is to give each topic its own independent consumer loop so they never block each other.

| Approach | Topic A blocks Topic B? | Kafka poll efficiency | Complexity |
| --- | --- | --- | --- |
| Multiconsumer, batch size 5 (before H2G-5999) | Yes — up to N × message time | Good (fewer polls) | Low |
| Multiconsumer, batch size 1 (H2G-5999 fix) | Yes — up to 1 × message time | Poor (max polls) | Low |
| Dedicated consumer per topic (correct fix) | **Never** | Good (batch size retained per topic) | Medium |

See the [ExcelMapping redesign document](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5385224223/HTGPlus.ExcelMapping.Subscriber+Architecture+Design) for the full target architecture using dedicated consumers with shared rule cache and parallel batch processing.

## Recommended Action

- Roll back the batch size removal from H2G-5999.
- Close or redesign H2G-5999 with the correct fix: replace the multiconsumer in ExcelMapping with dedicated consumers per topic.
- The multiconsumer with its original batch size is fine for the low-volume services it was designed for — only ExcelMapping needs to change.
