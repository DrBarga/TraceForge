"""Build the shareable data-handling brief from the published Markdown source."""

from pathlib import Path
import re
from xml.sax.saxutils import escape

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "output" / "pdf" / "TraceForge-data-handling.pdf"
INK = colors.HexColor("#172d3b")
MUTED = colors.HexColor("#55636a")
ACCENT = colors.HexColor("#b24f27")


def inline(text: str) -> str:
    value = escape(text)
    value = re.sub(r"\[([^\]]+)\]\((mailto:[^\s)]+|https://[^\s)]+)\)", r'<link href="\2" color="#b24f27">\1</link>', value)
    return re.sub(r"\*\*([^*]+)\*\*", r"<b>\1</b>", value)


def footer(canvas, document):
    canvas.saveState()
    width, height = A4
    canvas.setStrokeColor(colors.HexColor("#dcded7"))
    canvas.line(44, height - 33, width - 44, height - 33)
    canvas.setFont("BriefBold", 8)
    canvas.setFillColor(INK)
    canvas.drawString(44, height - 25, "TRACEFORGE / DATA HANDLING")
    canvas.setFont("Brief", 8)
    canvas.setFillColor(MUTED)
    canvas.drawRightString(width - 44, height - 25, "WINDOWS X64 / 1.0.0")
    canvas.line(44, 36, width - 44, 36)
    canvas.drawString(44, 23, "Bohdan Zelya  |  bogdan.zelya.s@gmail.com")
    canvas.drawRightString(width - 44, 23, f"1 October 2026  /  {document.page}")
    canvas.restoreState()


def main():
    fonts = Path("C:/Windows/Fonts")
    pdfmetrics.registerFont(TTFont("Brief", str(fonts / "arial.ttf")))
    pdfmetrics.registerFont(TTFont("BriefBold", str(fonts / "arialbd.ttf")))
    pdfmetrics.registerFontFamily("Brief", normal="Brief", bold="BriefBold")
    body = ParagraphStyle("Body", fontName="Brief", fontSize=9.3, leading=13.1, textColor=MUTED, spaceAfter=7, alignment=TA_LEFT)
    heading = ParagraphStyle("Heading", parent=body, fontName="BriefBold", fontSize=13.2, leading=17, textColor=INK, spaceBefore=12, spaceAfter=7, keepWithNext=True)
    title = ParagraphStyle("Title", parent=heading, fontSize=23, leading=28, spaceBefore=0, spaceAfter=14)
    bullet = ParagraphStyle("Bullet", parent=body, leftIndent=10, firstLineIndent=-8, spaceAfter=5)
    blocks = re.split(r"\n\s*\n", (ROOT / "docs" / "DATA_HANDLING.md").read_text(encoding="utf-8").strip())
    story = []
    for block in blocks:
        if block.startswith("# "):
            story.append(Paragraph(inline(block[2:]), title))
        elif block.startswith("## "):
            story.append(Paragraph(inline(block[3:]), heading))
        elif block.startswith("- "):
            for line in block.splitlines():
                if not line.startswith("- "):
                    raise ValueError("Unsupported list continuation")
                story.append(Paragraph("- " + inline(line[2:]), bullet))
            story.append(Spacer(1, 3))
        else:
            story.append(Paragraph(inline(block.replace("\n", " ")), body))
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    document = SimpleDocTemplate(str(OUTPUT), pagesize=A4, rightMargin=44, leftMargin=44, topMargin=52, bottomMargin=50, title="TraceForge 1.0.0 - data handling and security", author="Bohdan Zelya", subject="Collection, local storage, retention, sharing and protection of diagnostic data")
    document.build(story, onFirstPage=footer, onLaterPages=footer)
    print(f"Built {OUTPUT}")


if __name__ == "__main__":
    main()
