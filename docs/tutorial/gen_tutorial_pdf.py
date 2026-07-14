# -*- coding: utf-8 -*-
"""Render the tutorial lessons (docs/tutorial/lessons/*.md) into one book PDF.

A reportlab renderer in the same family as gen_qa_guide.py, but a fuller
Markdown engine tuned for a technical book: embedded Unicode fonts (so arrows
and box-drawing diagrams render), Pygments-highlighted code blocks, callout
boxes for blockquotes / "Architecture Decision" sections, a cover, and a
two-pass table of contents with real PDF bookmarks.

Pure Python + reportlab + pygments — no pandoc / weasyprint / wkhtmltopdf, so
it runs on this Windows box exactly like the QA generators. Fonts are the
system Segoe UI (body) + Consolas (code); both cover the glyph set this content
uses (measured: em/en dash, box-drawing, arrows, not-equal, middot, star).

Usage:  python docs/tutorial/gen_tutorial_pdf.py
Output: docs/tutorial/PEREZOSOFT_COURSE.pdf
"""
import os, re, html, glob, datetime
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.enums import TA_LEFT, TA_CENTER
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (BaseDocTemplate, PageTemplate, Frame, Paragraph,
                                Spacer, Table, TableStyle, KeepTogether, PageBreak,
                                HRFlowable, ListFlowable, ListItem, Flowable,
                                NextPageTemplate)
from reportlab.platypus.tableofcontents import TableOfContents

import pygments
from pygments import lex
from pygments.lexers import (CSharpLexer, YamlLexer, SqlLexer, JsonLexer,
                             BashLexer, TextLexer, get_lexer_by_name)
from pygments.token import Token

HERE = os.path.dirname(os.path.abspath(__file__))
LESSON_DIR = os.path.join(HERE, "lessons")
SAMPLE = os.path.join(HERE, "SAMPLE_LESSON_2.5.md")
OUT = os.path.join(HERE, "PEREZOSOFT_COURSE.pdf")

# ---------------------------------------------------------------------------
# Fonts — embed system TTFs that cover the measured glyph set.
# ---------------------------------------------------------------------------
WF = "C:/Windows/Fonts"
def _reg(name, fn):
    pdfmetrics.registerFont(TTFont(name, os.path.join(WF, fn)))

_reg("Body", "segoeui.ttf");   _reg("Body-Bold", "segoeuib.ttf")
_reg("Body-Italic", "segoeuii.ttf"); _reg("Body-BoldItalic", "segoeuiz.ttf")
_reg("Head", "segoeuib.ttf")
try:
    _reg("Head-Semi", "seguisb.ttf")
except Exception:
    pdfmetrics.registerFont(TTFont("Head-Semi", os.path.join(WF, "segoeuib.ttf")))
_reg("Mono", "consola.ttf"); _reg("Mono-Bold", "consolab.ttf")
pdfmetrics.registerFontFamily("Body", normal="Body", bold="Body-Bold",
                              italic="Body-Italic", boldItalic="Body-BoldItalic")
pdfmetrics.registerFontFamily("Mono", normal="Mono", bold="Mono-Bold",
                              italic="Mono", boldItalic="Mono-Bold")

INK   = colors.HexColor("#1a3b5d")   # brand dark blue (matches QA PDFs)
INK2  = colors.HexColor("#0f2740")
ACC   = colors.HexColor("#2563a8")
MUTE  = colors.HexColor("#555555")
CODEBG = colors.HexColor("#f4f6f8")
CODEBORDER = colors.HexColor("#d5dde5")
CALLBG = colors.HexColor("#eef3f8")
CALLBAR = colors.HexColor("#2563a8")
ADRBG = colors.HexColor("#f3eee2")   # warm parchment for Architecture Decision
ADRBAR = colors.HexColor("#b8873b")

# Glyphs we are unsure a chosen font covers -> normalize (rare: x2, x1).
NORMALIZE = [("►", ">"), ("❌", '<font color="#c0392b">x</font>')]

styles = getSampleStyleSheet()
def P(name, **kw):
    kw.setdefault("fontName", "Body")
    return ParagraphStyle(name, parent=styles["Normal"], **kw)

H1 = P("H1", fontName="Head", fontSize=22, leading=26, textColor=INK, spaceBefore=6, spaceAfter=10)
H2 = P("H2", fontName="Head-Semi", fontSize=15, leading=19, textColor=INK, spaceBefore=14, spaceAfter=5)
H3 = P("H3", fontName="Head-Semi", fontSize=12, leading=16, textColor=INK2, spaceBefore=10, spaceAfter=3)
H4 = P("H4", fontName="Body-Bold", fontSize=10.5, leading=14, textColor=INK2, spaceBefore=8, spaceAfter=2)
BODY = P("BODY", fontSize=10, leading=15, spaceAfter=7, alignment=TA_LEFT)
LI = P("LI", fontSize=10, leading=14.5, spaceAfter=3, alignment=TA_LEFT)
CODE = P("CODE", fontName="Mono", fontSize=8.2, leading=11.4, textColor=colors.HexColor("#1d2b36"))
CALL = P("CALL", fontSize=9.5, leading=14, textColor=colors.HexColor("#243b52"))
CALLLBL = P("CALLLBL", fontName="Body-Bold", fontSize=9, leading=12, textColor=CALLBAR, spaceAfter=2)
ADRLBL = P("ADRLBL", fontName="Body-Bold", fontSize=9, leading=12, textColor=colors.HexColor("#8a6420"), spaceAfter=2)
COVERT = P("COVERT", fontName="Head", fontSize=30, leading=36, textColor=INK, alignment=TA_CENTER)
COVERS = P("COVERS", fontName="Head-Semi", fontSize=14, leading=20, textColor=MUTE, alignment=TA_CENTER)

# ---------------------------------------------------------------------------
# Pygments syntax highlighting -> reportlab inline markup.
# ---------------------------------------------------------------------------
TOKEN_COLOR = {
    Token.Keyword: "#0b5fa5", Token.Keyword.Type: "#0b5fa5",
    Token.Name.Class: "#1a7f6b", Token.Name.Namespace: "#1a7f6b",
    Token.Name.Function: "#7a3ea8", Token.Name.Decorator: "#7a3ea8",
    Token.Name.Attribute: "#7a3ea8",
    Token.String: "#a03030", Token.String.Doc: "#a03030",
    Token.Literal.String: "#a03030", Token.Number: "#8a5000",
    Token.Comment: "#6a7d6a", Token.Comment.Single: "#6a7d6a",
    Token.Comment.Multiline: "#6a7d6a",
    Token.Operator: "#333333", Token.Punctuation: "#333333",
}
def _tok_color(tt):
    while tt is not Token:
        if tt in TOKEN_COLOR:
            return TOKEN_COLOR[tt]
        tt = tt.parent
    return "#1d2b36"

LEXERS = {"csharp": CSharpLexer, "cs": CSharpLexer, "c#": CSharpLexer,
          "yaml": YamlLexer, "yml": YamlLexer, "sql": SqlLexer,
          "json": JsonLexer, "sh": BashLexer, "bash": BashLexer, "shell": BashLexer}
def _lexer(lang):
    lang = (lang or "").strip().lower()
    if lang in LEXERS:
        return LEXERS[lang]()
    try:
        return get_lexer_by_name(lang) if lang else TextLexer()
    except Exception:
        return TextLexer()

def code_markup(source, lang):
    """Return reportlab markup preserving indentation and allowing wrap."""
    out = []
    for line in source.split("\n"):
        # split leading whitespace (-> nbsp, preserves indent) from the rest
        m = re.match(r"[ \t]*", line)
        indent = m.group(0).replace("\t", "    ")
        rest = line[m.end():]
        seg = ["&nbsp;" * len(indent)]
        for tt, val in lex(rest + "\n", _lexer(lang)):
            val = val.rstrip("\n")
            if not val:
                continue
            esc = html.escape(val)
            col = _tok_color(tt)
            seg.append('<font color="%s">%s</font>' % (col, esc))
        out.append("".join(seg))
    return "<br/>".join(out)

# ---------------------------------------------------------------------------
# Inline markdown: **bold** *italic* `code` [text](url)
# ---------------------------------------------------------------------------
def inline(text):
    text = text.strip()
    for src, repl in NORMALIZE:
        if repl.startswith("<"):   # already-markup replacement handled after escape
            continue
        text = text.replace(src, repl)
    parts = re.split(r'(`[^`]+`)', text)
    out = []
    for part in parts:
        if part.startswith("`") and part.endswith("`") and len(part) >= 2:
            out.append('<font name="Mono" size="9" color="#1d2b36">%s</font>'
                       % html.escape(part[1:-1]))
            continue
        seg = html.escape(part)
        seg = seg.replace("❌", '<font color="#c0392b">x</font>')
        seg = re.sub(r'\*\*([^*]+)\*\*', r'<b>\1</b>', seg)
        seg = re.sub(r'(?<!\*)\*([^*]+)\*(?!\*)', r'<i>\1</i>', seg)
        seg = re.sub(r'\[([^\]]+)\]\(([^)]+)\)',
                     r'<u><font color="#2563a8">\1</font></u>', seg)
        out.append(seg)
    return "".join(out)

# ---------------------------------------------------------------------------
# Callout box flowable (blockquote / Architecture Decision)
# ---------------------------------------------------------------------------
def callout(inner_flowables, bg, bar, label=None, label_style=None):
    body = []
    if label:
        body.append(Paragraph(label, label_style))
    body.extend(inner_flowables)
    inner = Table([[body]], colWidths=[150*mm])
    inner.setStyle(TableStyle([
        ("BACKGROUND", (0,0), (-1,-1), bg),
        ("LEFTPADDING", (0,0), (-1,-1), 9), ("RIGHTPADDING", (0,0), (-1,-1), 9),
        ("TOPPADDING", (0,0), (-1,-1), 7), ("BOTTOMPADDING", (0,0), (-1,-1), 7),
        ("LINEBEFORE", (0,0), (0,-1), 3, bar),
    ]))
    return inner

def code_box(source, lang):
    para = Paragraph(code_markup(source, lang), CODE)
    t = Table([[para]], colWidths=[150*mm])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0,0), (-1,-1), CODEBG),
        ("BOX", (0,0), (-1,-1), 0.6, CODEBORDER),
        ("LEFTPADDING", (0,0), (-1,-1), 7), ("RIGHTPADDING", (0,0), (-1,-1), 7),
        ("TOPPADDING", (0,0), (-1,-1), 6), ("BOTTOMPADDING", (0,0), (-1,-1), 6),
    ]))
    return t

# ---------------------------------------------------------------------------
# Markdown -> flowables (block parser tuned for these lessons)
# ---------------------------------------------------------------------------
_key = [0]
def next_key():
    _key[0] += 1
    return "h%d" % _key[0]

def parse_md(md, story, toc_level_base=1):
    lines = md.split("\n")
    i, N = 0, len(lines)
    while i < N:
        ln = lines[i]
        s = ln.rstrip()

        if not s.strip():
            i += 1; continue

        # fenced code
        m = re.match(r'^\s*```(\w+)?\s*$', s)
        if m:
            lang = m.group(1) or ""
            buf = []
            i += 1
            while i < N and not re.match(r'^\s*```\s*$', lines[i]):
                buf.append(lines[i]); i += 1
            i += 1
            story.append(code_box("\n".join(buf), lang))
            story.append(Spacer(1, 5))
            continue

        # headings
        m = re.match(r'^(#{1,4})\s+(.*)$', s)
        if m:
            level = len(m.group(1)); txt = m.group(2).strip()
            style = {1: H1, 2: H2, 3: H3, 4: H4}[level]
            key = next_key()
            p = Paragraph('<a name="%s"/>%s' % (key, inline(txt)), style)
            p._toc = (level, txt, key)   # picked up in afterFlowable
            story.append(p)
            i += 1; continue

        # horizontal rule
        if re.match(r'^\s*---+\s*$', s) or re.match(r'^\s*___+\s*$', s):
            story.append(Spacer(1, 3))
            story.append(HRFlowable(width="100%", thickness=0.5, color=CODEBORDER))
            story.append(Spacer(1, 5))
            i += 1; continue

        # blockquote (callout) — gather consecutive '>' lines
        if s.lstrip().startswith(">"):
            buf = []
            while i < N and lines[i].lstrip().startswith(">"):
                buf.append(re.sub(r'^\s*>\s?', '', lines[i])); i += 1
            inner_md = "\n".join(buf)
            is_adr = bool(re.search(r'architecture decision|\bthe fork\b', inner_md, re.I))
            inner = []
            parse_md(inner_md, inner)
            if is_adr:
                story.append(callout(inner, ADRBG, ADRBAR, "ARCHITECTURE DECISION", ADRLBL))
            else:
                story.append(callout(inner, CALLBG, CALLBAR, None, None))
            story.append(Spacer(1, 6))
            continue

        # table
        if s.lstrip().startswith("|") and i+1 < N and re.match(r'^\s*\|?[\s:|-]+\|', lines[i+1]):
            rows = []
            while i < N and lines[i].lstrip().startswith("|"):
                rows.append(lines[i]); i += 1
            story.append(md_table(rows))
            story.append(Spacer(1, 6))
            continue

        # unordered list
        if re.match(r'^\s*[-*]\s+', s):
            items = []
            while i < N and re.match(r'^\s*[-*]\s+', lines[i].rstrip()):
                txt = re.sub(r'^\s*[-*]\s+', '', lines[i].rstrip())
                items.append(ListItem(Paragraph(inline(txt), LI), leftIndent=14, value="•"))
                i += 1
            story.append(ListFlowable(items, bulletType="bullet", start="•",
                                      leftIndent=10, bulletColor=ACC))
            story.append(Spacer(1, 4))
            continue

        # ordered list
        if re.match(r'^\s*\d+\.\s+', s):
            items = []
            while i < N and re.match(r'^\s*\d+\.\s+', lines[i].rstrip()):
                txt = re.sub(r'^\s*\d+\.\s+', '', lines[i].rstrip())
                items.append(ListItem(Paragraph(inline(txt), LI), leftIndent=16))
                i += 1
            story.append(ListFlowable(items, bulletType="1", leftIndent=12,
                                      bulletColor=INK, bulletFontName="Body-Bold"))
            story.append(Spacer(1, 4))
            continue

        # paragraph — gather until blank / block start
        buf = [s]
        i += 1
        while i < N and lines[i].strip() and not re.match(
                r'^\s*(#{1,4}\s|```|>|[-*]\s|\d+\.\s|---+\s*$|\|)', lines[i]):
            buf.append(lines[i].rstrip()); i += 1
        story.append(Paragraph(inline(" ".join(buf)), BODY))

def md_table(rows):
    def cells(r):
        return [c.strip() for c in r.strip().strip("|").split("|")]
    head = cells(rows[0])
    body = [cells(r) for r in rows[2:]]
    ncol = len(head)
    CELL = P("CELL", fontSize=8.5, leading=11)
    CELH = P("CELH", fontName="Body-Bold", fontSize=8.5, leading=11, textColor=colors.white)
    data = [[Paragraph(inline(c), CELH) for c in head]]
    for r in body:
        r = (r + [""]*ncol)[:ncol]
        data.append([Paragraph(inline(c), CELL) for c in r])
    avail = 150*mm
    t = Table(data, colWidths=[avail/ncol]*ncol, repeatRows=1)
    t.setStyle(TableStyle([
        ("BACKGROUND", (0,0), (-1,0), INK),
        ("ROWBACKGROUNDS", (0,1), (-1,-1), [colors.white, colors.HexColor("#eef2f6")]),
        ("GRID", (0,0), (-1,-1), 0.4, CODEBORDER),
        ("VALIGN", (0,0), (-1,-1), "TOP"),
        ("LEFTPADDING", (0,0), (-1,-1), 5), ("RIGHTPADDING", (0,0), (-1,-1), 5),
        ("TOPPADDING", (0,0), (-1,-1), 4), ("BOTTOMPADDING", (0,0), (-1,-1), 4),
    ]))
    return t

# ---------------------------------------------------------------------------
# Document assembly: cover, TOC, lessons, page furniture, bookmarks.
# ---------------------------------------------------------------------------
PART_TITLES = {
    "0": "Part 0 — Orientation", "1": "Part 1 — The walking skeleton",
    "2": "Part 2 — Identity & tenancy", "3": "Part 3 — The slice pattern & the UI",
    "4": "Part 4 — Reliability & operations", "5": "Part 5 — Monetization",
    "6": "Part 6 — B2B essentials & security", "7": "Part 7 — Compliance & extensibility",
    "8": "Part 8 — Ship it", "9": "Part 9 — Make it yours", "A": "Appendix",
}

def lesson_files():
    files = []
    for f in glob.glob(os.path.join(LESSON_DIR, "*.md")):
        base = os.path.basename(f)
        m = re.match(r'^(\d+|A)\.(\d+)-', base)
        if m:
            major = 10 if m.group(1) == "A" else int(m.group(1))  # "A.x" appendix sorts after Part 9
            files.append((major, int(m.group(2)), f))
    files.sort()
    return files

class Book(BaseDocTemplate):
    def __init__(self, path, **kw):
        super().__init__(path, pagesize=A4,
                         leftMargin=30*mm, rightMargin=30*mm,
                         topMargin=22*mm, bottomMargin=20*mm, **kw)
        frame = Frame(self.leftMargin, self.bottomMargin,
                      self.width, self.height, id="main")
        self.addPageTemplates([
            PageTemplate(id="cover", frames=[frame], onPage=self._blank),
            PageTemplate(id="body", frames=[frame], onPage=self._furniture),
        ])
        self.cur_part = ""      # e.g. "Part 2 — Identity & tenancy"  (header, left)
        self.cur_lesson = ""    # e.g. "2.6 · Tenancy I: the global query filter"  (header, right)

    def _blank(self, canvas, doc): pass

    def _furniture(self, canvas, doc):
        canvas.saveState()
        canvas.setFillColor(MUTE)
        # header breadcrumb — Part (left) · Lesson (right): "you are here" on every page
        canvas.setFont("Body-Bold", 8)
        left = self.cur_part or "Perezosoft Platform · a build-from-scratch course"
        canvas.drawString(30*mm, A4[1]-14*mm, left[:64])
        if self.cur_lesson:
            canvas.setFont("Body", 8)
            canvas.drawRightString(A4[0]-30*mm, A4[1]-14*mm, self.cur_lesson[:70])
        canvas.setStrokeColor(CODEBORDER)
        canvas.line(30*mm, A4[1]-16*mm, A4[0]-30*mm, A4[1]-16*mm)
        # footer — centred page number
        canvas.setFont("Body", 8)
        canvas.drawCentredString(A4[0]/2, 12*mm, str(doc.page))
        canvas.restoreState()

    def afterFlowable(self, flowable):
        # Part-divider pages carry a _part tag: set the left breadcrumb, clear the lesson.
        part = getattr(flowable, "_part", None)
        if part is not None:
            self.cur_part = part
            self.cur_lesson = ""
            return
        toc = getattr(flowable, "_toc", None)
        if toc:
            level, text, key = toc
            if level <= 2:
                self.notify("TOCEntry", (level-1, text, self.page, key))
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(text, key, level=min(level-1, 3), closed=(level>1))
            # Breadcrumb: a lesson H1 ("Lesson 2.6 — Title") sets Part + Lesson and holds
            # them across the whole lesson; section H2s no longer clobber the header.
            if level == 1:
                m = re.match(r'^Lesson\s+(\d+|A)\.(\d+)\s*[—–:\-]\s*(.*)$', text)
                if m:
                    self.cur_part = PART_TITLES.get(m.group(1), self.cur_part)
                    self.cur_lesson = f"{m.group(1)}.{m.group(2)} · {m.group(3)}"
                else:
                    self.cur_part = text   # front-matter section (Preface, etc.)
                    self.cur_lesson = ""

def build():
    doc = Book(OUT)
    story = []
    today = datetime.date.today().isoformat()

    # ---- cover ----
    story.append(Spacer(1, 55*mm))
    story.append(Paragraph("Build a Production SaaS Platform", COVERT))
    story.append(Paragraph("From Scratch", COVERT))
    story.append(Spacer(1, 8*mm))
    story.append(Paragraph("A rebuild-from-zero course in architecture, "
                           "engineering practice, and the decisions behind them", COVERS))
    story.append(Spacer(1, 40*mm))
    story.append(Paragraph("Perezosoft Platform &nbsp;·&nbsp; generated %s" % today,
                           P("cd", fontSize=9, textColor=MUTE, alignment=TA_CENTER)))
    story.append(NextPageTemplate("body"))
    story.append(PageBreak())

    # ---- TOC ----
    story.append(Paragraph("Contents", H1))
    toc = TableOfContents()
    toc.levelStyles = [
        P("toc0", fontName="Head-Semi", fontSize=11, leading=18, textColor=INK, spaceBefore=6),
        P("toc1", fontSize=9.5, leading=14, leftIndent=12, textColor=INK2),
    ]
    story.append(toc)
    story.append(PageBreak())

    # ---- front matter (preface etc.) ----
    fm = os.path.join(HERE, "FRONTMATTER.md")
    if os.path.exists(fm):
        parse_md(open(fm, encoding="utf-8").read(), story)
        story.append(PageBreak())

    # ---- lessons, grouped by part ----
    files = lesson_files()
    cur_part = None
    for major, minor, path in files:
        part_key = "A" if major >= 10 else str(major)
        if part_key != cur_part:
            cur_part = part_key
            story.append(PageBreak())
            story.append(Spacer(1, 30*mm))
            part_title = PART_TITLES.get(part_key, "Part " + part_key)
            div = Paragraph(part_title, P("pt", fontName="Head", fontSize=24, leading=30,
                                          textColor=INK, alignment=TA_CENTER))
            div._part = part_title   # picked up in afterFlowable to set the header breadcrumb
            story.append(div)
            story.append(PageBreak())
        md = open(path, encoding="utf-8").read()
        parse_md(md, story)
        story.append(PageBreak())

    doc.multiBuild(story)
    print("wrote", OUT)

if __name__ == "__main__":
    build()
