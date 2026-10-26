#!/usr/bin/env python3
"""Generate authored cached-SmartArt controls from the hashed public #154 template.

Usage: python3 cached-diagrams.py /path/to/fdo87488.docx /tmp/diagram-probes
The template is not downloaded and no binary fixtures are committed.
"""
import argparse
import hashlib
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED
import xml.etree.ElementTree as ET

A = "http://schemas.openxmlformats.org/drawingml/2006/main"
D = "http://schemas.microsoft.com/office/drawing/2008/diagram"
W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
REL = "http://schemas.openxmlformats.org/package/2006/relationships"
ET.register_namespace("a", A)
ET.register_namespace("dsp", D)
ET.register_namespace("w", W)
ET.register_namespace("wp", WP)


def transform(x=0, y=0, width=3810000, height=2540000, extra=""):
    return f'<a:xfrm {extra}><a:off x="{x}" y="{y}"/><a:ext cx="{width}" cy="{height}"/></a:xfrm>'


def text(value="node", size=1800, family="Liberation Sans", properties="", paragraph="algn=\"l\""):
    return f'<a:p><a:pPr {paragraph}><a:lnSpc><a:spcPct val="100000"/></a:lnSpc><a:spcBef><a:spcPts val="0"/></a:spcBef><a:spcAft><a:spcPts val="0"/></a:spcAft></a:pPr><a:r><a:rPr sz="{size}" {properties}><a:latin typeface="{family}"/></a:rPr><a:t>{value}</a:t></a:r></a:p>'


def shape(label="node", geometry="rect", coordinates=None, paint=None, body=None, frame=None, kind="sp"):
    if paint is None:
        paint = '<a:solidFill><a:srgbClr val="0070C0"/></a:solidFill><a:ln><a:noFill/></a:ln>'
    if frame is None:
        frame = 'lIns="0" tIns="0" rIns="0" bIns="0" anchor="t"'
    return f'<dsp:{kind}><dsp:nvSpPr><dsp:cNvPr id="1" name="node"/><dsp:cNvSpPr/></dsp:nvSpPr><dsp:spPr>{coordinates or transform()}<a:prstGeom prst="{geometry}"><a:avLst/></a:prstGeom>{paint}</dsp:spPr><dsp:txBody><a:bodyPr {frame}/><a:lstStyle/>{body if body is not None else text(label)}</dsp:txBody></dsp:{kind}>'


def cache(shapes, group=""):
    root = ET.fromstring(f'<dsp:drawing xmlns:dsp="{D}" xmlns:a="{A}"><dsp:spTree><dsp:nvGrpSpPr><dsp:cNvPr id="0" name=""/><dsp:cNvGrpSpPr/></dsp:nvGrpSpPr><dsp:grpSpPr>{group}</dsp:grpSpPr>{shapes}</dsp:spTree></dsp:drawing>')
    for index, node in enumerate(root.iter(f"{{{D}}}sp"), 1):
        node.set("modelId", "{00000000-0000-0000-0000-%012X}" % index)
    for index, node in enumerate(root.iter(f"{{{D}}}cNvPr")):
        node.set("id", str(index))
    return ET.tostring(root)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("template", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    manifest = Path(__file__).resolve().parents[1] / "selections" / "issue-154.sha256"
    expected = next(line.split()[0] for line in manifest.read_text().splitlines() if line.endswith("  ooxmlimport/data/fdo87488.docx"))
    if hashlib.sha256(args.template.read_bytes()).hexdigest() != expected:
        parser.error("Template SHA-256 does not match the recorded issue-154 original")
    args.output.mkdir(parents=True, exist_ok=True)
    with ZipFile(args.template) as source:
        parts = {name: source.read(name) for name in source.namelist()}
    half = 1905000
    no_paint = '<a:noFill/><a:ln><a:noFill/></a:ln>'
    group = '<a:xfrm><a:off x="0" y="0"/><a:ext cx="1905000" cy="1270000"/><a:chOff x="100" y="200"/><a:chExt cx="2000" cy="1000"/></a:xfrm>'
    nested = '<dsp:grpSp><dsp:nvGrpSpPr><dsp:cNvPr id="2" name="group"/><dsp:cNvGrpSpPr/></dsp:nvGrpSpPr><dsp:grpSpPr>' + transform(635000, 508000, 1270000, 1270000).replace('</a:xfrm>', '<a:chOff x="0" y="0"/><a:chExt cx="2540000" cy="2540000"/></a:xfrm>') + '</dsp:grpSpPr>' + shape("nested", coordinates=transform(254000, 254000, 2032000, 2032000)) + '</dsp:grpSp>'
    gradient = '<a:gradFill><a:gsLst><a:gs pos="0"><a:srgbClr val="00FF00"/></a:gs><a:gs pos="100000"><a:srgbClr val="0000FF"/></a:gs></a:gsLst><a:lin ang="5400000" scaled="0"/></a:gradFill><a:ln><a:noFill/></a:ln>'
    emphasis = '<a:p><a:pPr><a:defRPr b="1" i="1" u="sng"/></a:pPr><a:r><a:rPr sz="3200"><a:latin typeface="Liberation Sans"/></a:rPr><a:t>Emphasis</a:t></a:r></a:p>'
    colored = '<a:p><a:r><a:rPr sz="3200"><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill><a:latin typeface="Liberation Sans"/></a:rPr><a:t>Red </a:t></a:r><a:r><a:rPr sz="3200"><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill><a:latin typeface="Liberation Sans"/></a:rPr><a:t>Blue</a:t></a:r></a:p>'
    probes = {
        "01-cache-link": (shape(), ""),
        "02-siblings": (shape("left", coordinates=transform(width=half)) + shape("right", coordinates=transform(x=half, width=half), paint='<a:solidFill><a:srgbClr val="00AA00"/></a:solidFill>'), ""),
        "03-coordinate-space": (shape("scaled", coordinates=transform(1100, 700, 500, 250), body=""), group),
        "04-nested-group": (nested, ""),
        "05-ellipse": (shape("ellipse", geometry="ellipse"), ""),
        "06-connector": (shape(geometry="line", coordinates=transform(extra='flipH="1"'), body="", paint='<a:noFill/><a:ln w="101600"><a:solidFill><a:srgbClr val="000000"/></a:solidFill></a:ln>', kind="sp"), ""),
        "07-gradient": (shape("gradient", paint=gradient), ""),
        "08-outline": (shape("outline", paint='<a:noFill/><a:ln w="127000"><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill></a:ln>'), ""),
        "09-cached-text": (shape(paint=no_paint, body=text("first", size=3200).replace('</a:p>', '<a:br/><a:fld id="{11111111-1111-1111-1111-111111111111}" type="custom"><a:rPr sz="3200"><a:latin typeface="Liberation Sans"/></a:rPr><a:t>cached</a:t></a:fld></a:p>') + text("second", size=3200)), ""),
        "10-paragraph-align": (shape(paint=no_paint, body=text("Centered", size=3200, paragraph='algn="ctr"')), ""),
        "11-font-size": (shape(paint=no_paint, body=text("Serif 24", size=2400, family="Liberation Serif")), ""),
        "12-emphasis": (shape(paint=no_paint, body=emphasis), ""),
        "13-text-color": (shape(paint=no_paint, body=colored), ""),
        "14-frame-insets": (shape("Frame", body=text("Frame", size=3200), frame='lIns="317500" tIns="127000" rIns="127000" bIns="127000" anchor="b"'), ""),
        "15-block-flow": (shape("block", coordinates=transform(height=6350000)), ""),
    }
    hashes = []
    for name, (shapes, group_transform) in probes.items():
        modified = dict(parts)
        document = ET.fromstring(parts["word/document.xml"])
        body = document.find(f"{{{W}}}body")
        paragraphs = body.findall(f"{{{W}}}p")
        section = body.find(f"{{{W}}}sectPr")
        first, second = paragraphs[0], paragraphs[1]
        for child in list(body):
            body.remove(child)
        body.append(first)
        if name == "15-block-flow":
            body.append(second)
            after = ET.SubElement(body, f"{{{W}}}p")
            ET.SubElement(ET.SubElement(after, f"{{{W}}}r"), f"{{{W}}}t").text = "AFTER"
        body.append(section)
        for extent in body.iter(f"{{{WP}}}extent"):
            extent.set("cx", "3810000")
            extent.set("cy", "6350000" if name == "15-block-flow" else "2540000")
        modified["word/document.xml"] = ET.tostring(document, encoding="utf-8", xml_declaration=True)
        modified["word/diagrams/drawing1.xml"] = cache(shapes, group_transform)
        if name == "15-block-flow":
            modified["word/diagrams/drawing2.xml"] = cache(shapes)
        if name == "01-cache-link":
            modified["word/diagrams/custom-cache.xml"] = modified.pop("word/diagrams/drawing1.xml")
            rels = ET.fromstring(modified["word/_rels/document.xml.rels"])
            for relationship in rels:
                if relationship.get("Id") == "rId10":
                    relationship.set("Target", "diagrams/custom-cache.xml")
            modified["word/_rels/document.xml.rels"] = ET.tostring(rels)
            content_types = ET.fromstring(modified["[Content_Types].xml"])
            for override in content_types:
                if override.get("PartName") == "/word/diagrams/drawing1.xml":
                    override.set("PartName", "/word/diagrams/custom-cache.xml")
            modified["[Content_Types].xml"] = ET.tostring(content_types)
        path = args.output / (name + ".docx")
        with ZipFile(path, "w") as output:
            for part, content in modified.items():
                info = ZipInfo(part, date_time=(1980, 1, 1, 0, 0, 0))
                info.compress_type = ZIP_DEFLATED
                info.external_attr = 0o600 << 16
                output.writestr(info, content)
        hashes.append(hashlib.sha256(path.read_bytes()).hexdigest() + "  " + path.name)
    (args.output / "samples.txt").write_text("".join(name + ".docx\n" for name in probes), encoding="utf-8")
    (args.output / "samples.sha256").write_text("\n".join(hashes) + "\n", encoding="utf-8")
    print(f"Generated {len(probes)} cached diagram controls in {args.output}")


if __name__ == "__main__":
    main()
