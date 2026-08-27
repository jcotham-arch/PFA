# Phase 21 — Machine-Discovered Behavior Research

Phase 21 adds a research-only path for unnamed combinations of existing, point-in-time market features. It does not activate strategies, submit sandbox signals, or provide a broker route.

## What is implemented

- Immutable, versioned discovery manifests freeze the dataset, correction revision, temporal split, embargo, feature list, cluster count, seed, search version, correction method, and run-time knowledge boundary.
- A framework-neutral deterministic feature-cluster engine creates `FeatureCluster_*` hypotheses without selecting or depending on an external ML framework.
- Training observations alone define the discovery population. Evaluation observations are assigned out of sample and never alter the model.
- Point-in-time checks reject features known after the decision, outcomes unavailable when the run began, mixed datasets, and mixed correction revisions.
- Every declared cluster is retained, including insufficient, candidate, positive, and negative results. There is no result suppression.
- Each cluster records a centroid, training/evaluation sample counts, train/evaluation R, raw and Bonferroni-adjusted values, and ranked feature explanations.
- Machine results are adapted into ordinary `GeneralResearchRun` / `ResearchHypothesis` records with `FamilyId = FeatureCluster`.
- SQLite persistence stores immutable run/model/search metadata and immutable cluster artifacts. Database constraints keep activation flags false.
- Read-only research endpoints expose capabilities and persisted runs. Public mutation remains disabled.

## Safety and interpretation

The simple deterministic clustering implementation is a reproducible research primitive, not a claim that clusters are tradable or statistically validated. Its conservative score is recorded as research metadata and must not be interpreted as a production-grade significance test. Machine hypotheses receive no privileged promotion path; they must pass the same evidence, validation, governance, and forward-operation stages as human-defined hypotheses.

No human-defined pattern module, existing research contract, strategy lifecycle gate, database schema behavior outside the additive Phase 21 tables, configuration, secret, provider contract, or API behavior was changed.

## Rollback

Disable registration of the machine-discovery engine, repository, and controller. Existing run and cluster records can remain as retained research evidence. No strategy or execution state depends on this subsystem.
