# -*- coding: utf-8 -*-
"""Generate a printable QA run-log PDF from QA_TEST_PLAN.md case headers."""
import re, datetime
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.platypus import (BaseDocTemplate, PageTemplate, Frame, Paragraph,
                                Spacer, Table, TableStyle, NextPageTemplate, FrameBreak)
from reportlab.lib.enums import TA_LEFT

PLAN = "QA_TEST_PLAN.md"
OUT = "QA_RUN_LOG.pdf"

PRI = {"\U0001f534": "Smoke", "\U0001f7e0": "Core", "\U0001f7e2": "Edge"}
PRI_COLOR = {"Smoke": "#c0392b", "Core": "#d35400", "Edge": "#27ae60"}

# --- suite section map: prefix -> (number, title) in document order ---
SUITES = [
    ("4",  "Smoke suite", ["QA-SMK"]),
    ("5",  "Web — Authentication", ["QA-AUTH"]),
    ("6",  "Web — Onboarding & new tenant", ["QA-ONB"]),
    ("7",  "Web — Household management", ["QA-HH"]),
    ("8",  "Web — Invitations & joining", ["QA-INV"]),
    ("9",  "Web — Settings / linked accounts", ["QA-SET"]),
    ("10", "Web — Localization (i18n)", ["QA-I18N"]),
    ("11", "Emails (Mailpit)", ["QA-MAIL"]),
    ("12", "Desktop — MAUI / Windows", ["QA-DSK"]),
    ("13", "Android — MAUI", ["QA-AND"]),
    ("14", "Cross-cutting security", ["QA-SEC"]),
]

hdr_re = re.compile(r'^### (QA-[A-Z0-9]+-\d+) — (.+)$')

def parse():
    cases = []
    for line in open(PLAN, encoding="utf-8"):
        m = hdr_re.match(line.rstrip())
        if not m:
            continue
        cid, rest = m.group(1), m.group(2)
        pri = "Core"
        for emo, name in PRI.items():
            if emo in rest:
                pri = name
                break
        # client from trailing (...)
        cm = re.search(r'\(([^()]*)\)\s*$', rest)
        client = cm.group(1) if cm else ""
        if "see QA-" in rest:
            client += " — alias"
        # strip emoji + trailing client paren + alias note for the title
        title = rest
        for emo in PRI:
            title = title.replace(emo, "")
        title = re.sub(r'\s*— see QA-[A-Z0-9-]+\s*$', '', title)
        title = re.sub(r'\s*\([^()]*\)\s*$', '', title).strip()
        cases.append(dict(id=cid, title=title, pri=pri, client=client))
    return cases

def suite_for(cid):
    for num, name, prefixes in SUITES:
        for p in prefixes:
            if cid.startswith(p + "-"):
                return (num, name)
    return ("?", "Other")

cases = parse()

styles = getSampleStyleSheet()
H1 = ParagraphStyle("H1", parent=styles["Title"], fontSize=18, spaceAfter=2, leading=22)
SUB = ParagraphStyle("SUB", parent=styles["Normal"], fontSize=8.5, textColor=colors.HexColor("#555555"), leading=11)
SEC = ParagraphStyle("SEC", parent=styles["Heading2"], fontSize=11, spaceBefore=8, spaceAfter=3,
                     textColor=colors.HexColor("#1a3b5d"))
CELL = ParagraphStyle("CELL", parent=styles["Normal"], fontSize=8, leading=9.5)
CELLB = ParagraphStyle("CELLB", parent=CELL, fontName="Helvetica-Bold")
# Header-row cells sit on the dark-blue band: a TableStyle TEXTCOLOR does NOT
# recolor a Paragraph flowable, so the text must carry white in its own style.
CELLH = ParagraphStyle("CELLH", parent=CELLB, textColor=colors.white)
SMALL = ParagraphStyle("SMALL", parent=styles["Normal"], fontSize=7.5, leading=9.5)
NOTE = ParagraphStyle("NOTE", parent=styles["Normal"], fontSize=8, leading=11)

today = datetime.date.today().isoformat()

def header_footer(canvas, doc):
    canvas.saveState()
    canvas.setFont("Helvetica", 7.5)
    canvas.setFillColor(colors.HexColor("#888888"))
    canvas.drawString(15*mm, 8*mm, "QA Run Log — generated from QA_TEST_PLAN.md")
    canvas.drawRightString(A4[0]-15*mm, 8*mm, "Page %d" % canvas.getPageNumber())
    canvas.restoreState()

doc = BaseDocTemplate(OUT, pagesize=A4,
                      leftMargin=14*mm, rightMargin=14*mm,
                      topMargin=14*mm, bottomMargin=14*mm)
frame = Frame(doc.leftMargin, doc.bottomMargin,
              doc.width, doc.height, id="main")
doc.addPageTemplates([PageTemplate(id="main", frames=[frame], onPage=header_footer)])

story = []
story.append(Paragraph("QA Run Log", H1))
story.append(Paragraph(
    "Printable sign-off sheet — one row per test case from <b>docs/QA_TEST_PLAN.md</b>. "
    "Mark each case <b>P</b>ass / <b>F</b>ail / <b>B</b>locked / <b>N-A</b>. "
    "Run the Smoke suite first; if any Smoke case fails, stop and report.", SUB))
story.append(Spacer(1, 4*mm))

# --- run metadata block ---
meta = [
    [Paragraph("<b>Tester</b>", CELL), Paragraph("&nbsp;", CELL),
     Paragraph("<b>Date</b>", CELL), Paragraph(today, CELL)],
    [Paragraph("<b>Build / commit SHA</b>", CELL), Paragraph("&nbsp;", CELL),
     Paragraph("<b>Branch</b>", CELL), Paragraph("&nbsp;", CELL)],
    [Paragraph("<b>Client(s) under test</b>", CELL), Paragraph("Web ☐&nbsp;&nbsp; Desktop ☐&nbsp;&nbsp; Android ☐", CELL),
     Paragraph("<b>Mail → Mailpit?</b>", CELL), Paragraph("Yes ☐&nbsp;&nbsp; No ☐", CELL)],
]
mt = Table(meta, colWidths=[34*mm, 60*mm, 30*mm, 58*mm])
mt.setStyle(TableStyle([
    ("BOX", (0,0), (-1,-1), 0.6, colors.HexColor("#888888")),
    ("INNERGRID", (0,0), (-1,-1), 0.3, colors.HexColor("#cccccc")),
    ("VALIGN", (0,0), (-1,-1), "MIDDLE"),
    ("BACKGROUND", (0,0), (0,-1), colors.HexColor("#f2f5f8")),
    ("BACKGROUND", (2,0), (2,-1), colors.HexColor("#f2f5f8")),
    ("TOPPADDING", (0,0), (-1,-1), 4), ("BOTTOMPADDING", (0,0), (-1,-1), 4),
]))
story.append(mt)
story.append(Spacer(1, 5*mm))

# --- counts ---
n = len(cases)
sm = sum(1 for c in cases if c["pri"]=="Smoke")
co = sum(1 for c in cases if c["pri"]=="Core")
ed = sum(1 for c in cases if c["pri"]=="Edge")
story.append(Paragraph(
    "<b>%d cases</b> &nbsp;—&nbsp; <font color='#c0392b'>● %d Smoke</font> &nbsp; "
    "<font color='#d35400'>● %d Core</font> &nbsp; <font color='#27ae60'>● %d Edge</font>"
    % (n, sm, co, ed), NOTE))
story.append(Spacer(1, 3*mm))

# --- per-suite tables ---
RESULT_HDR = "Result (circle one)"
col_widths = [20*mm, 56*mm, 13*mm, 17*mm, 34*mm, 42*mm]

def result_cell():
    return Paragraph("P&nbsp;&nbsp;/&nbsp;&nbsp;F&nbsp;&nbsp;/&nbsp;&nbsp;B&nbsp;&nbsp;/&nbsp;&nbsp;N-A", CELL)

last_suite = None
for c in cases:
    num, name = suite_for(c["id"])
    key = (num, name)
    if key != last_suite:
        last_suite = key
        story.append(Spacer(1, 2*mm))
        story.append(Paragraph("§%s &nbsp; %s" % (num, name), SEC))
        head = [Paragraph(h, CELLH) for h in
                ["Case ID", "Title", "Pri", "Client", RESULT_HDR, "Notes / defect link"]]
        rows = [head]
        # accumulate rows per suite then flush — build incrementally
        story.append(("__TABLE_MARK__", rows))
    # find current rows list (last marker)
    for el in reversed(story):
        if isinstance(el, tuple) and el[0]=="__TABLE_MARK__":
            rows = el[1]
            break
    pri_cell = Paragraph("<font color='%s'>●</font> %s" % (PRI_COLOR[c["pri"]], c["pri"]), SMALL)
    rows.append([
        Paragraph(c["id"], CELLB),
        Paragraph(c["title"], CELL),
        pri_cell,
        Paragraph(c["client"], SMALL),
        result_cell(),
        Paragraph("&nbsp;", CELL),
    ])

# Now convert markers into real Tables
final = []
for el in story:
    if isinstance(el, tuple) and el[0]=="__TABLE_MARK__":
        rows = el[1]
        t = Table(rows, colWidths=col_widths, repeatRows=1)
        ts = [
            ("GRID", (0,0), (-1,-1), 0.3, colors.HexColor("#bbbbbb")),
            ("BACKGROUND", (0,0), (-1,0), colors.HexColor("#1a3b5d")),
            ("TEXTCOLOR", (0,0), (-1,0), colors.white),
            ("VALIGN", (0,0), (-1,-1), "MIDDLE"),
            ("TOPPADDING", (0,0), (-1,-1), 3), ("BOTTOMPADDING", (0,0), (-1,-1), 3),
            ("LEFTPADDING", (0,0), (-1,-1), 4), ("RIGHTPADDING", (0,0), (-1,-1), 4),
            ("ROWBACKGROUNDS", (0,1), (-1,-1), [colors.white, colors.HexColor("#f6f8fa")]),
        ]
        t.setStyle(TableStyle(ts))
        final.append(t)
    else:
        final.append(el)

final.append(Spacer(1, 5*mm))
final.append(Paragraph("Release gate", SEC))
final.append(Paragraph(
    "All ● <b>Smoke</b> + all ● <b>Core</b> cases Pass on Web; ● Smoke Pass on Desktop "
    "and Android; no open Critical/High defects. ● Edge cases triaged (Pass or "
    "accepted-known-issue).", NOTE))
final.append(Spacer(1, 4*mm))
gate = [[Paragraph("<b>Overall verdict</b>", CELL),
         Paragraph("PASS ☐&nbsp;&nbsp;&nbsp; FAIL ☐&nbsp;&nbsp;&nbsp; CONDITIONAL ☐", CELL),
         Paragraph("<b>Signed</b>", CELL), Paragraph("&nbsp;", CELL)]]
gt = Table(gate, colWidths=[28*mm, 78*mm, 20*mm, 56*mm])
gt.setStyle(TableStyle([
    ("BOX", (0,0), (-1,-1), 0.6, colors.HexColor("#888888")),
    ("INNERGRID", (0,0), (-1,-1), 0.3, colors.HexColor("#cccccc")),
    ("VALIGN", (0,0), (-1,-1), "MIDDLE"),
    ("BACKGROUND", (0,0), (0,-1), colors.HexColor("#f2f5f8")),
    ("BACKGROUND", (2,0), (2,-1), colors.HexColor("#f2f5f8")),
    ("TOPPADDING", (0,0), (-1,-1), 6), ("BOTTOMPADDING", (0,0), (-1,-1), 6),
]))
final.append(gt)

doc.build(final)
print("wrote", OUT, "with", n, "cases")
