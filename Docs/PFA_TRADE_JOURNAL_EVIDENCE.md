# External trade-journal evidence

Version: `trade-journal-import-1.0.0`

## Purpose

External execution journals are behavioral evidence: they describe what a trader actually did, including successful and unsuccessful trades. They are not strategy recommendations or ground-truth labels. The research system uses them to identify repeated entry, sizing, timing, holding, exit, and loss-management behavior and later align those episodes with market state known at entry.

An imported journal cannot activate a strategy or route an order. Any hypothesis learned from it must independently pass chronological development gates, untouched testing, and prospective sandbox validation.

## Privacy and provenance

The importer hashes account and order identifiers before persistence. It stores the source file name, SHA-256 content identity, importer version, source row number, normalized execution record, reconstructed episode, and immutable content hashes. Reimporting identical file content returns the same import rather than duplicating evidence. The original CSV is not copied into the repository.

The supported execution export maps movement codes as follows:

- `1`: open long
- `2`: close long
- `3`: open short
- `4`: close short

Quantity signs are validated against movement type. Provider timestamps retain their explicit UTC offset and are normalized to UTC. Rows sharing account, contract, and provider `created_on` identity form one episode, preserving partial exits. Gross P&L derives from reported point-contract movement and the MES/ES contract multiplier; estimated costs are gross minus reported net P&L.

## Private result handling

Source journals, content fingerprints, import identifiers, account-specific metrics, and behavioral findings remain in the authorized local research database. They are intentionally excluded from source control. Public documentation describes only the generic research method and safety contract.

## API

- `POST /api/research/trade-journals`: local-development multipart CSV import.
- `GET /api/research/trade-journals`: immutable import manifests.
- `GET /api/research/trade-journals/{importId}/episodes`: normalized reconstructed episodes.

The next layer aligns each episode with canonical bars, detected patterns, sequences, and point-in-time context available at entry, then compares winners and losers and runs counterfactual entry/stop/target policies without rewriting the source history.

## Point-in-time market alignment

Version `trade-journal-market-alignment-1.3.0` associates an episode only with canonical bars completed by entry, registered observations known during the preceding 30 minutes, and terminology-neutral structural events whose known-at clock is no later than entry. It records direction agreement, recency, source identity, and immutable episode-level evidence. Directional segments use exactly the latest known instance of each signal family per episode so conflicting earlier signals cannot place one episode in both aligned and opposed rows.

Reports include broad pattern and structural-event summaries plus aligned-versus-opposed directional segments. These are hypothesis generators from trader-selected behavior, not independent strategy validation. Account-specific reports and results remain local. Candidate segments must be reformulated as explicit entry, stop, target, management, and abstention rules and replayed on the full chronologically partitioned market corpus before prospective sandbox testing.

Trend continuation, pullback/retracement, and mean reversion are registered as planned detector families. Breakout and failed-breakout research remains active. Spread trading is treated separately as a future multi-leg strategy family because it requires synchronized leg execution, roll-aware pricing, costs, margin, and portfolio risk rather than a single-market pattern detector.
