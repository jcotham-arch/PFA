from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import letter, landscape
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import inch
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfbase import pdfmetrics
from reportlab.platypus import (
    BaseDocTemplate, Frame, PageTemplate, Paragraph, Spacer, Table, TableStyle,
    PageBreak, KeepTogether, Flowable, HRFlowable
)

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "output" / "pdf" / "PFA_Architecture_Vision_and_Status.pdf"
OUT.parent.mkdir(parents=True, exist_ok=True)

NAVY = colors.HexColor("#102A43")
BLUE = colors.HexColor("#1769AA")
PALE_BLUE = colors.HexColor("#EAF4FB")
GREEN = colors.HexColor("#147D4F")
PALE_GREEN = colors.HexColor("#E9F7F0")
RED = colors.HexColor("#B42318")
PALE_RED = colors.HexColor("#FDECEA")
GOLD = colors.HexColor("#D99A19")
INK = colors.HexColor("#243B53")
MUTED = colors.HexColor("#627D98")
GRID = colors.HexColor("#CBD5E1")
WHITE = colors.white

font_dir = Path("C:/Windows/Fonts")
if (font_dir / "segoeui.ttf").exists():
    pdfmetrics.registerFont(TTFont("Segoe", str(font_dir / "segoeui.ttf")))
    pdfmetrics.registerFont(TTFont("Segoe-Bold", str(font_dir / "segoeuib.ttf")))
    BODY_FONT, BOLD_FONT = "Segoe", "Segoe-Bold"
else:
    BODY_FONT, BOLD_FONT = "Helvetica", "Helvetica-Bold"

styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name="TitlePFA", fontName=BOLD_FONT, fontSize=28, leading=32, textColor=WHITE, spaceAfter=14))
styles.add(ParagraphStyle(name="SubtitlePFA", fontName=BODY_FONT, fontSize=12, leading=17, textColor=colors.HexColor("#D9EAF7")))
styles.add(ParagraphStyle(name="H1PFA", fontName=BOLD_FONT, fontSize=19, leading=23, textColor=NAVY, spaceBefore=2, spaceAfter=10))
styles.add(ParagraphStyle(name="H2PFA", fontName=BOLD_FONT, fontSize=13, leading=16, textColor=BLUE, spaceBefore=8, spaceAfter=5))
styles.add(ParagraphStyle(name="BodyPFA", fontName=BODY_FONT, fontSize=9.2, leading=13.2, textColor=INK, spaceAfter=6))
styles.add(ParagraphStyle(name="SmallPFA", fontName=BODY_FONT, fontSize=7.7, leading=10.5, textColor=INK))
styles.add(ParagraphStyle(name="TinyPFA", fontName=BODY_FONT, fontSize=6.8, leading=8.6, textColor=INK))
styles.add(ParagraphStyle(name="CalloutPFA", fontName=BOLD_FONT, fontSize=10, leading=14, textColor=NAVY))
styles.add(ParagraphStyle(name="CenterPFA", fontName=BOLD_FONT, fontSize=8, leading=10, alignment=TA_CENTER, textColor=INK))
styles.add(ParagraphStyle(name="WhiteSmall", fontName=BODY_FONT, fontSize=8.5, leading=12, textColor=WHITE))
styles.add(ParagraphStyle(name="WhiteBold", fontName=BOLD_FONT, fontSize=9, leading=12, textColor=WHITE))


def P(text, style="BodyPFA"):
    return Paragraph(text, styles[style])


def status_label(done=True):
    color = GREEN if done else RED
    word = "GREEN - IMPLEMENTED" if done else "RED - REQUIRED"
    return Paragraph(f'<font color="{color.hexval()}"><b>{word}</b></font>', styles["SmallPFA"])


def bullets(items, color=INK):
    out = []
    for item in items:
        out += [P(f'<font color="{color.hexval()}">&#8226;</font> {item}'), Spacer(1, 1)]
    return out


class ArchitectureFlow(Flowable):
    def __init__(self, width=520, height=232):
        super().__init__()
        self.width, self.height = width, height

    def draw(self):
        c = self.canv
        c.saveState()
        c.scale(0.75, 0.75)
        nodes = [
            (20, 245, 118, 38, "Market providers", True),
            (165, 245, 118, 38, "Raw + canonical data", True),
            (310, 245, 118, 38, "Features + state", True),
            (455, 245, 118, 38, "Patterns + sequences", True),
            (165, 165, 118, 38, "Research + outcomes", True),
            (310, 165, 118, 38, "Evidence + validation", True),
            (455, 165, 118, 38, "Strategy registry", True),
            (165, 85, 118, 38, "Training pipeline", False),
            (310, 85, 118, 38, "Prospective sandbox", True),
            (455, 85, 118, 38, "Risk + governance", True),
            (600, 85, 78, 38, "Live pilot", False),
        ]
        arrows = [
            ((138,264),(165,264)), ((283,264),(310,264)), ((428,264),(455,264)),
            ((514,245),(224,203)), ((283,184),(310,184)), ((428,184),(455,184)),
            ((224,165),(224,123)), ((369,165),(369,123)), ((514,165),(514,123)),
            ((283,104),(310,104)), ((428,104),(455,104)), ((573,104),(600,104)),
        ]
        c.setStrokeColor(MUTED); c.setLineWidth(1.4)
        for a,b in arrows:
            c.line(a[0],a[1],b[0],b[1])
            c.circle(b[0],b[1],1.8,stroke=0,fill=1)
        for x,y,w,h,label,done in nodes:
            fill = PALE_GREEN if done else PALE_RED
            stroke = GREEN if done else RED
            c.setFillColor(fill); c.setStrokeColor(stroke); c.roundRect(x,y,w,h,6,fill=1,stroke=1)
            c.setFillColor(stroke); c.setFont(BOLD_FONT, 7.2)
            c.drawCentredString(x+w/2,y+h-11,"GREEN" if done else "RED")
            c.setFillColor(INK); c.setFont(BOLD_FONT, 7.5)
            words = label.split()
            if len(label) > 18:
                cut = len(words)//2
                c.drawCentredString(x+w/2,y+14," ".join(words[:cut]))
                c.drawCentredString(x+w/2,y+5," ".join(words[cut:]))
            else:
                c.drawCentredString(x+w/2,y+8,label)
        c.setFont(BODY_FONT, 7.3); c.setFillColor(MUTED)
        c.drawString(20, 56, "Green means the subsystem exists in code and is covered by the current test suite.")
        c.drawString(20, 44, "Red means the end-state capability is not operationally complete, even if contracts or readiness gates exist.")
        c.restoreState()


class StatusRoadmap(Flowable):
    def __init__(self, width=690, height=350):
        super().__init__(); self.width, self.height = width, height
    def draw(self):
        c=self.canv
        stages=[
            ("Foundation", "0-6", True, "Golden masters, instruments, canonical timeline, feature state, pattern contracts, FVG adapter, universal records"),
            ("Intelligence", "7-14", True, "Sequences, liquidity sweeps, breakouts, registry, research, cross-day/market evidence, ambiguity"),
            ("Validation", "15-17", True, "Historical jobs, walk-forward contracts, order-flow foundation"),
            ("Safe operation", "18-22", True, "Virtual ledger, governance, forward campaigns, discovery, certification and readiness gates"),
            ("Corpus build", "Next", False, "Finish multi-asset collection, roll/session policy, replay detections and outcomes, generate valid labels"),
            ("Agent research", "Next", False, "Train baselines, temporal evaluation, calibration, regime robustness, frozen model registry"),
            ("Pilot", "Later", False, "Long-running paper proof, broker certification, reconciliation, credentials, drills, explicit approval"),
        ]
        y=320
        for name,phase,done,desc in stages:
            col=GREEN if done else RED; fill=PALE_GREEN if done else PALE_RED
            c.setFillColor(col); c.circle(38,y,12,fill=1,stroke=0)
            c.setFillColor(WHITE); c.setFont(BOLD_FONT,7); c.drawCentredString(38,y-2,"OK" if done else "TODO")
            c.setStrokeColor(col); c.setLineWidth(3)
            if y>55: c.line(38,y-12,38,y-43)
            c.setFillColor(fill); c.setStrokeColor(col); c.roundRect(65,y-23,600,46,6,fill=1,stroke=1)
            c.setFillColor(col); c.setFont(BOLD_FONT,9); c.drawString(78,y+7,f"{name}  |  Phase {phase}")
            c.setFillColor(INK); c.setFont(BODY_FONT,7.5)
            c.drawString(78,y-9,desc[:112])
            y-=48


def header_footer(canvas, doc):
    canvas.saveState()
    if doc.page > 1:
        canvas.setStrokeColor(GRID); canvas.line(42, 35, 570, 35)
        canvas.setFont(BODY_FONT, 7); canvas.setFillColor(MUTED)
        canvas.drawString(42, 23, "PFA Architecture, Vision and Delivery Status - 28 Aug 2026")
        canvas.drawRightString(570, 23, f"Page {doc.page}")
    canvas.restoreState()


doc = BaseDocTemplate(str(OUT), pagesize=letter, leftMargin=42, rightMargin=42, topMargin=42, bottomMargin=44,
                      title="PFA Architecture, Vision and Delivery Status")
frame = Frame(doc.leftMargin, doc.bottomMargin, doc.width, doc.height, id="normal")
doc.addPageTemplates([PageTemplate(id="all", frames=[frame], onPage=header_footer)])

story=[]

# Cover
cover = Table([[P("PROP FIRM ASSASSINS", "WhiteBold")],
               [P("Architecture, Vision<br/>and Delivery Status", "TitlePFA")],
               [P("A repository-backed explanation of what has been built, what remains, and the intended end game.", "SubtitlePFA")],
               [Spacer(1, 150)],
               [P("Prepared 28 August 2026", "WhiteSmall")]], colWidths=[doc.width], rowHeights=[30, 102, 58, 220, 30])
cover.setStyle(TableStyle([("BACKGROUND",(0,0),(-1,-1),NAVY),("BOX",(0,0),(-1,-1),0,NAVY),
                           ("LEFTPADDING",(0,0),(-1,-1),28),("RIGHTPADDING",(0,0),(-1,-1),28),
                           ("TOPPADDING",(0,0),(-1,-1),12),("VALIGN",(0,0),(-1,-1),"MIDDLE")]))
story += [cover, PageBreak()]

story += [P("Executive alignment summary", "H1PFA")]
story += [P("PFA is no longer merely an FVG scanner. It is now a tested research and safety platform whose durable subject is the market itself: point-in-time market facts are captured once, then patterns, sequences, strategies, validation, sandbox behavior, and future agents are built as versioned annotations over that history.")]
origin = Table([[P("How it started", "CalloutPFA"), P("The project did not begin as a designed visual product. It began with working analytical code and a simple request: turn this code into a visual app. The first interface appeared rapidly, then continued evolving in step with each new research and safety capability. That visual feedback loop is now part of the product method: the app makes the engine's evidence, limitations, and progress inspectable as the architecture grows.")]], colWidths=[1.35*inch,5.95*inch])
origin.setStyle(TableStyle([("BACKGROUND",(0,0),(-1,-1),PALE_BLUE),("LINEBEFORE",(0,0),(0,-1),4,BLUE),("VALIGN",(0,0),(-1,-1),"TOP"),("PADDING",(0,0),(-1,-1),9)]))
story += [origin, Spacer(1,10)]
summary = [
    [P("Current proof", "WhiteBold"), P("Meaning", "WhiteBold")],
    [P("29 implementation commits"), P("A staged migration from regression protection through certification and training readiness.")],
    [P("24,595 tracked lines"), P("22,665 C# lines, plus UI, JSON fixtures, and 1,567 lines of design/operations documentation.")],
    [P("221 tests passing"), P("No failed or skipped tests; last verified build had zero warnings and zero errors.")],
    [P("29 commits awaiting push"), P("The work is committed locally on <b>pfa-market-intelligence</b>; remote publication is still pending explicit destination approval.")],
]
t=Table(summary,colWidths=[1.65*inch,5.65*inch],repeatRows=1)
t.setStyle(TableStyle([("BACKGROUND",(0,0),(-1,0),NAVY),("GRID",(0,0),(-1,-1),0.5,GRID),
                       ("VALIGN",(0,0),(-1,-1),"TOP"),("LEFTPADDING",(0,0),(-1,-1),8),
                       ("RIGHTPADDING",(0,0),(-1,-1),8),("TOPPADDING",(0,0),(-1,-1),7),("BOTTOMPADDING",(0,0),(-1,-1),7),
                       ("BACKGROUND",(0,1),(-1,-1),colors.white)]))
story += [t, Spacer(1,12)]
call=Table([[P("The key distinction", "CalloutPFA"), P("The architecture is broadly implemented; the evidence corpus and trained agent are not. We have built the factory, safety rails, and inspection stations. The next job is to run enough correctly versioned market data through that factory to produce trustworthy training examples.")]], colWidths=[1.5*inch,5.8*inch])
call.setStyle(TableStyle([("BACKGROUND",(0,0),(-1,-1),PALE_BLUE),("BOX",(0,0),(-1,-1),1,BLUE),("VALIGN",(0,0),(-1,-1),"TOP"),("PADDING",(0,0),(-1,-1),10)]))
story += [call, Spacer(1,12), P("End game", "H2PFA")]
story += bullets([
    "Continuously capture reliable multi-market futures data with explicit contracts, sessions, provenance, revisions, and quality.",
    "Turn that timeline into reproducible state, feature, pattern, sequence, and outcome records without hindsight leakage.",
    "Train research agents on immutable temporal datasets and evaluate them on unseen, walk-forward, then prospective paper evidence.",
    "Help traders navigate prop-firm rules with calibrated decisions, including <b>NO TRADE</b>, while keeping billing separate from safety authority.",
    "Only after sustained evidence and explicit approval, permit a bounded MES paper/live pilot behind independent risk, reconciliation, credentials, audit, and kill-switch controls."
])
story += [PageBreak()]

story += [P("Architecture map: target and present state", "H1PFA"), ArchitectureFlow(), Spacer(1,4)]
story += [P("The horizontal flow is intentional: data precedes interpretation; interpretation precedes evidence; evidence precedes authorization. The training pipeline consumes historical evidence but cannot promote itself. The live-pilot node remains red because readiness projections and certification simulations are not a functioning broker route.")]
story += [P("Delivery roadmap", "H2PFA"), StatusRoadmap()]
story += [PageBreak()]

story += [P("What has actually been built", "H1PFA")]
done_rows = [
    ("Regression and compatibility", "Golden masters preserve legacy FVG detection, aggregation, replay, scenarios, research, persistence, and version behavior."),
    ("Market identity", "Versioned futures instruments, contract identity, sessions, tick sizes, point values, and supported resolutions replace silent MES assumptions."),
    ("Canonical timeline", "Raw events and canonical bars carry timestamps, provenance, quality, revisions, corrections, and deterministic identity across live and backfill paths."),
    ("Market intelligence", "Point-in-time market state and feature definitions are separated from trading judgment and preserve KnownAtUtc semantics."),
    ("Pattern modules", "FVG, liquidity sweep, range breakout, and failed breakout detectors share generalized contracts and module registration."),
    ("Universal evidence records", "Observations, lifecycle, outcomes, metrics, events, lineage, and quality are stored independently of any one scanner."),
    ("Sequence intelligence", "Versioned definitions replay ordered, overlapping, partial, failed, and completed market behavior without rewriting source observations."),
    ("Research and validation", "General research, cross-day and cross-market evidence, explicit execution ambiguity, out-of-sample and walk-forward contracts are present."),
    ("Historical operations", "Durable, resumable historical jobs, windows, checkpoints, coverage, and a multi-asset campaign are represented."),
    ("Order-flow foundation", "Canonical order-flow events, classification, snapshots, persistence, and service boundaries exist; production source coverage remains separate."),
    ("Sandbox and governance", "Append-only virtual ledgers, conservative fills, risk policy, approval/veto decisions, emergency stop, incidents, and default-deny controls exist."),
    ("Forward and machine research", "Prospective campaign contracts, degradation monitoring, reproducible discovery runs, and non-privileged hypotheses exist."),
    ("Certification", "Immutable $50K prop-firm rule packs and campaigns model latency, costs, drawdown, consistency, payout gates, and explicitly deny live routing."),
    ("Product modularity", "Core, Agent Research Lab, Live Agent, Advanced Strategies, coaching, and BYO-agent module contracts separate entitlement from safety."),
    ("Agent readiness", "Point-in-time training-example contracts, leakage rejection, dataset splits, and a readiness gate of 100 R labels over 90 days exist."),
]
rows=[[P("Status","WhiteBold"),P("Capability","WhiteBold"),P("Concrete implementation","WhiteBold")]]
for name,desc in done_rows: rows.append([status_label(True),P(name,"SmallPFA"),P(desc,"SmallPFA")])
t=Table(rows,colWidths=[1.18*inch,1.55*inch,4.55*inch],repeatRows=1)
t.setStyle(TableStyle([("BACKGROUND",(0,0),(-1,0),NAVY),("GRID",(0,0),(-1,-1),0.35,GRID),
                       ("VALIGN",(0,0),(-1,-1),"TOP"),("LEFTPADDING",(0,0),(-1,-1),5),("RIGHTPADDING",(0,0),(-1,-1),5),
                       ("TOPPADDING",(0,0),(-1,-1),5),("BOTTOMPADDING",(0,0),(-1,-1),5)]))
story += [t, PageBreak()]

story += [P("Code architecture by layer", "H1PFA")]
layers=[
    ("API and product surfaces", "Controllers expose data health, coverage, patterns, sequences, research, validation, sandbox, governance, certification, modules, and agent readiness. Browser screens support the market-intelligence view, sandbox decisions, certification, and Agent & Module Center."),
    ("Services and engines", "Services orchestrate ingestion, aggregation, replay, detection, generic outcomes, sequence replay, research campaigns, scenario normalization, validation, sandbox operation, certification, governance, and training readiness."),
    ("Domain contracts", "Versioned records define instruments, bars, features, patterns, outcomes, sequences, strategies, evidence, sandbox events, governance decisions, certification campaigns, product modules, and training examples."),
    ("Persistence", "SQLite repositories preserve raw data, candles, canonical timeline, observations, outcomes, sequences, research runs, evidence, strategies, historical jobs, order flow, sandbox ledgers, governance, validation, discovery, and certification."),
    ("Tests", "Golden-master and phase-specific tests assert deterministic identities, point-in-time chronology, idempotency, data isolation, ambiguity handling, nonactivation, safety gates, and database invariants."),
    ("Documentation", "Architecture, migration, each phase, the research universe, agent lab, product UI, modular product model, owner direction, multi-asset campaign, and partner handoff are recorded as reviewable decisions."),
]
for name,desc in layers:
    box=Table([[P(name,"CalloutPFA"),P(desc)]],colWidths=[1.65*inch,5.65*inch])
    box.setStyle(TableStyle([("BACKGROUND",(0,0),(-1,-1),PALE_GREEN),("LINEBEFORE",(0,0),(0,-1),4,GREEN),("VALIGN",(0,0),(-1,-1),"TOP"),("PADDING",(0,0),(-1,-1),8)]))
    story += [box,Spacer(1,7)]
story += [P("Why this matters", "H2PFA")]
story += [P("The code is not 24,595 lines of trading logic. Most of its value is separation and traceability: the same market fact can be replayed through multiple detectors; a detector cannot silently become a strategy; a promising backtest cannot silently become an order; and a future agent cannot train on information that was unavailable at decision time.")]
story += [PageBreak()]

story += [P("Remaining work - red until proven complete", "H1PFA")]
todo_rows = [
    ("Finish and verify the corpus", "Complete the active multi-asset backfill; reconcile failures, gaps, duplicates, sparse contracts, provider limits, and authoritative database coverage."),
    ("Contract continuity policy", "Approve versioned rollover selection, adjusted/unadjusted series behavior, and liquidity rules. Current dated contracts must not masquerade as continuous futures."),
    ("Session/calendar policy", "Finalize CME trading dates, maintenance breaks, holidays, and early closes, then rebuild coverage and evidence under the approved version."),
    ("Outcome labeling at scale", "Replay every active detector across supported timeframes, generate generic outcomes, then define strategy-specific entry/stop/target/R labels with costs and chronology."),
    ("Feature completeness", "Add and validate market structure, displacement, session references, volume/volatility, and production-grade cross-market context where definition-pending."),
    ("Training dataset manifests", "Materialize immutable train/validation/test manifests, frozen revisions, embargo rules, deduplication, leakage audit, and reproducible feature schemas."),
    ("Train baseline agents", "Start with transparent baselines; measure calibration, expectancy after costs, drawdown, tail loss, stability, abstention quality, and regime degradation."),
    ("Model registry and reproducibility", "Persist model code/data versions, hyperparameters, seed, artifacts, metrics, approvals, and immutable lineage. No model may self-promote."),
    ("Prospective paper operation", "Run frozen strategies/models continuously in virtual accounts long enough to compare historical expectations with real forward behavior."),
    ("Production data operations", "Add scheduler/service hosting, restart recovery, monitoring, alerts, backups, retention, archival, reconciliation, and capacity planning beyond local SQLite assumptions."),
    ("Identity, billing, and entitlement", "Implement authenticated users, payment webhooks, server-side entitlements, signed partner assertions, revocation, audit, and least-privilege data scopes."),
    ("External module integration", "Build and validate the Advanced Strategies API manifest, DTOs, compatibility, health, idempotency, security, and failure isolation."),
    ("Execution semantics", "Approve fees, spread, slippage, latency, partial fills, order types, OCO ownership, cancel/replace, overlapping positions, and portfolio risk."),
    ("Broker paper certification", "Select an officially supported provider; implement credential custody, broker-neutral routing, idempotency, reconciliation, rejected/partial fills, and failure drills."),
    ("Bounded live pilot approval", "Only after stable walk-forward and nonzero forward evidence: explicit owner/governance authorization for MES, maximum one micro contract, with kill switch and incident ownership."),
]
rows=[[P("Status","WhiteBold"),P("Required capability","WhiteBold"),P("Definition of remaining work","WhiteBold")]]
for name,desc in todo_rows: rows.append([status_label(False),P(name,"SmallPFA"),P(desc,"SmallPFA")])
t=Table(rows,colWidths=[1.12*inch,1.62*inch,4.54*inch],repeatRows=1)
t.setStyle(TableStyle([("BACKGROUND",(0,0),(-1,0),NAVY),("GRID",(0,0),(-1,-1),0.35,GRID),
                       ("VALIGN",(0,0),(-1,-1),"TOP"),("LEFTPADDING",(0,0),(-1,-1),5),("RIGHTPADDING",(0,0),(-1,-1),5),
                       ("TOPPADDING",(0,0),(-1,-1),5),("BOTTOMPADDING",(0,0),(-1,-1),5)]))
story += [t, PageBreak()]

story += [P("Immediate path to agent training", "H1PFA")]
steps=[
    ("1", "Close the data loop", "Resume and finish the active 17-market dated-contract campaign. Treat database row counts and time coverage as authority, not provider-response counts."),
    ("2", "Freeze semantic decisions", "Version contract rollover, CME sessions, corrections, source priority, and feature definitions before creating a canonical training release."),
    ("3", "Replay observations", "Run FVG, liquidity sweep, range breakout, failed breakout, and approved sequences over every eligible instrument/timeframe partition."),
    ("4", "Generate labels", "Produce fixed-horizon generic outcomes first. Add strategy-specific R labels only from frozen execution hypotheses with explicit costs and ambiguity handling."),
    ("5", "Publish Dataset V1", "Create an immutable manifest: IDs, feature schema, time bounds, revisions, exclusions, hashes, label availability, and temporal splits."),
    ("6", "Train transparent baselines", "Benchmark simple rules, logistic/linear models, and calibrated tree models before complex agents. A model must beat honest baselines after costs."),
    ("7", "Walk forward", "Evaluate untouched folds and regimes. Track calibration, net expectancy, maximum drawdown, tail loss, turnover, cost sensitivity, and NO TRADE behavior."),
    ("8", "Prospective sandbox", "Freeze the winning candidate and operate it without retraining on its evaluation period. Compare expected and realized distributions."),
]
for n,title,desc in steps:
    row=Table([[P(n,"WhiteBold"),P(title,"CalloutPFA"),P(desc)]],colWidths=[0.4*inch,1.55*inch,5.35*inch])
    row.setStyle(TableStyle([("BACKGROUND",(0,0),(0,0),RED),("BACKGROUND",(1,0),(-1,0),PALE_RED),
                             ("VALIGN",(0,0),(-1,-1),"MIDDLE"),("ALIGN",(0,0),(0,0),"CENTER"),
                             ("PADDING",(0,0),(-1,-1),7),("BOX",(0,0),(-1,-1),0.5,colors.HexColor("#E9B7B2"))]))
    story += [row,Spacer(1,6)]
story += [Spacer(1,6), P("Earliest honest training start", "H2PFA")]
story += [P("Training can begin as soon as a first immutable, temporally valid dataset contains enough labels to support a meaningful split. The current code gate uses at least 100 distinct R-labeled outcomes spanning 90 days. That is a research eligibility floor, not a claim of adequacy, profitability, or permission to trade.")]
story += [PageBreak()]

story += [P("Phase-by-phase delivery ledger", "H1PFA")]
phase_data=[
    ("0", "Golden masters", True),("1", "Instruments, sessions, contracts", True),("2", "Canonical timeline", True),
    ("3", "Market state and features", True),("4", "Pattern contracts", True),("5", "FVG module adapter", True),
    ("6", "Universal observations/outcomes", True),("7", "Sequence intelligence", True),("8", "Liquidity sweep", True),
    ("9", "Breakout modules", True),("10", "Strategy registry", True),("11", "General research", True),
    ("12", "Cross-day evidence", True),("13", "Cross-market evidence", True),("14", "Ambiguity escalation", True),
    ("15", "Historical pipeline", True),("16", "Walk-forward validation", True),("17", "Order-flow foundation", True),
    ("18", "Virtual sandbox", True),("19", "Risk and governance", True),("20", "Forward campaigns", True),
    ("21", "Machine discovery", True),("22", "Certification/readiness infrastructure", True),
    ("23", "Operational corpus completion", False),("24", "Agent dataset + baseline training", False),
    ("25", "Sustained forward paper evidence", False),("26", "Broker-certified bounded pilot", False),
]
rows=[[P("Phase","WhiteBold"),P("Deliverable","WhiteBold"),P("State","WhiteBold")]]
for phase,name,done in phase_data: rows.append([P(phase,"SmallPFA"),P(name,"SmallPFA"),status_label(done)])
t=Table(rows,colWidths=[0.7*inch,4.7*inch,1.9*inch],repeatRows=1)
t.setStyle(TableStyle([("BACKGROUND",(0,0),(-1,0),NAVY),("GRID",(0,0),(-1,-1),0.35,GRID),
                       ("VALIGN",(0,0),(-1,-1),"TOP"),("ROWBACKGROUNDS",(0,1),(-1,-1),[colors.white,colors.HexColor("#F7FAFC")]),
                       ("LEFTPADDING",(0,0),(-1,-1),7),("RIGHTPADDING",(0,0),(-1,-1),7),("TOPPADDING",(0,0),(-1,-1),5),("BOTTOMPADDING",(0,0),(-1,-1),5)]))
story += [t, PageBreak()]

story += [P("Vision decisions for owner review", "H1PFA")]
decisions=[
    ("Market scope", "Is the 17-root futures universe the right initial research boundary, or should the first training release narrow to MES/MNQ plus a small context set?"),
    ("Agent job", "Should the first agent predict market outcomes, rank setups, recommend NO TRADE, coach prop-firm behavior, or combine these as separately evaluated heads?"),
    ("Primary objective", "Which objective governs model selection: net expectancy after costs, drawdown control, challenge-pass probability, payout probability, calibration, or a constrained combination?"),
    ("Time horizon", "What decision cadence and holding horizons define the first trainable product: intraday 1m/5m, higher-timeframe context, or both?"),
    ("Rollover", "Should training use explicit dated contracts only, a versioned unadjusted continuous series, adjusted continuous history, or multiple views?"),
    ("Evidence threshold", "What minimum sample, date span, regimes, walk-forward folds, and forward-paper duration are required before a candidate advances?"),
    ("Product boundary", "Is the core commercial value intelligence and coaching, a research agent, a live assistant, partner strategies, or a modular bundle with distinct safety gates?"),
    ("Pilot authority", "Confirm that MES, one micro contract maximum, and paper-first remain hard ceilings until a separately documented live approval."),
]
for title,q in decisions:
    story += [KeepTogether([P(title,"H2PFA"),P(q),HRFlowable(width="100%",thickness=0.5,color=GRID,spaceBefore=2,spaceAfter=8)])]
story += [Spacer(1,5)]
note=Table([[P("Alignment test", "CalloutPFA"),P("If the intended vision differs on the agent's job, target customer, primary objective, or live-trading boundary, revise those decisions before scaling data collection or training. Those choices determine labels, datasets, evaluation, UI, and governance.")]],colWidths=[1.35*inch,5.95*inch])
note.setStyle(TableStyle([("BACKGROUND",(0,0),(-1,-1),PALE_BLUE),("BOX",(0,0),(-1,-1),1,BLUE),("PADDING",(0,0),(-1,-1),10),("VALIGN",(0,0),(-1,-1),"TOP")]))
story += [note, PageBreak()]

story += [P("Safety invariants that should not change", "H1PFA")]
story += bullets([
    "Patterns describe market behavior; they are not strategies.",
    "Training labels and outcomes cannot appear in predictors before they were knowable.",
    "A paid entitlement grants product access, never risk approval or broker authority.",
    "Research, validation, walk-forward, and prospective paper periods remain separate populations.",
    "Ambiguous execution is unresolved or conservative; it is never optimistically backfilled.",
    "NO TRADE is a first-class decision and must be evaluated.",
    "Discovery proposes; evidence tests; governance promotes. The agent cannot promote itself.",
    "Every material dataset, feature, pattern, strategy, fill model, rule pack, and model is versioned and reproducible.",
    "Dated futures contracts remain explicit unless a reviewed rollover/adjustment policy creates a continuous series.",
    "Live execution remains absent until official provider support, credentials, reconciliation, duplicate protection, failure drills, independent governance, and explicit owner approval all exist."
])
story += [Spacer(1,12), P("Repository evidence used", "H2PFA")]
story += [P("This report was derived from the current branch history through commit <b>aacb7ba</b>, the complete tracked source tree, passing build/test results, Architecture V1, Migration Plan V1, Agent Research Lab V1, phase documents, modular product architecture, multi-asset campaign plan, owner direction, and the Advanced Strategies handoff. Status colors indicate implementation presence and verification, not production readiness or profitability.")]
story += [Spacer(1,16)]
final=Table([[P("Recommended owner decision", "WhiteBold")],[P("Approve or revise the end-game statement and the eight vision decisions in this report. Once aligned, the immediate engineering priority is corpus completion -> immutable Dataset V1 -> baseline training -> walk-forward evidence -> prospective paper operation.","WhiteSmall")]],colWidths=[doc.width])
final.setStyle(TableStyle([("BACKGROUND",(0,0),(-1,-1),NAVY),("PADDING",(0,0),(-1,-1),12)]))
story += [final]

doc.build(story)
print(OUT)
