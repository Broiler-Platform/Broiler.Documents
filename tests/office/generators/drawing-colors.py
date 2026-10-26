#!/usr/bin/env python3
"""Generate deterministic, self-contained DOCX color controls for native comparison.

Uses only Python's standard library. These are authored probes, not baseline
expectations; run both CLIs with the same LibreOffice, DPI and fonts.
"""
import argparse
import hashlib
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED

PROBES = {
    '01-scrgb': '<a:scrgbClr r="0" g="100000" b="0"/>',
    '02-hsl': '<a:hslClr hue="14400000" sat="100000" lum="50000"/>',
    '03-preset': '<a:prstClr val="orange"/>',
    '04-alpha-mod': '<a:srgbClr val="FF0000"><a:alphaMod val="0"/></a:srgbClr>',
    '05-alpha-off': '<a:srgbClr val="FF0000"><a:alphaOff val="-100000"/></a:srgbClr>',
    '06-hue': '<a:srgbClr val="FF0000"><a:hue val="7200000"/></a:srgbClr>',
    '07-hue-off': '<a:srgbClr val="FF0000"><a:hueOff val="7200000"/></a:srgbClr>',
    '08-hue-mod': '<a:srgbClr val="00FF00"><a:hueMod val="50000"/></a:srgbClr>',
    '09-sat': '<a:srgbClr val="FF0000"><a:sat val="0"/></a:srgbClr>',
    '10-sat-off': '<a:srgbClr val="FF0000"><a:satOff val="-100000"/></a:srgbClr>',
    '11-lum': '<a:srgbClr val="FF0000"><a:lum val="25000"/></a:srgbClr>',
    '12-comp': '<a:srgbClr val="FF0000"><a:comp/></a:srgbClr>',
    '13-inv': '<a:srgbClr val="808080"><a:inv/></a:srgbClr>',
    '14-gray': '<a:srgbClr val="FF0000"><a:gray/></a:srgbClr>',
    '15-gamma': '<a:srgbClr val="808080"><a:gamma/></a:srgbClr>',
    '16-inv-gamma': '<a:srgbClr val="808080"><a:invGamma/></a:srgbClr>',
    '17-hue-underflow': '<a:srgbClr val="FF0000"><a:hueOff val="-3600000"/></a:srgbClr>',
    '18-hue-overflow': '<a:srgbClr val="0000FF"><a:hueOff val="10800000"/></a:srgbClr>',
    '19-hue-mod-overflow': '<a:srgbClr val="00FF00"><a:hueMod val="400000"/></a:srgbClr>',
}


def package(color):
    document = f'''<?xml version="1.0" encoding="UTF-8"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
 xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
 xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
 xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape">
 <w:body><w:p><w:r><w:drawing>
 <wp:anchor distT="0" distB="0" distL="0" distR="0" simplePos="0" relativeHeight="0"
  behindDoc="1" locked="0" layoutInCell="1" allowOverlap="1">
  <wp:simplePos x="0" y="0"/>
  <wp:positionH relativeFrom="page"><wp:posOffset>914400</wp:posOffset></wp:positionH>
  <wp:positionV relativeFrom="page"><wp:posOffset>914400</wp:posOffset></wp:positionV>
  <wp:extent cx="5486400" cy="6400800"/><wp:wrapNone/><wp:docPr id="1" name="Color control"/>
  <a:graphic><a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape">
   <wps:wsp><wps:cNvSpPr/><wps:spPr><a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
    <a:solidFill>{color}</a:solidFill><a:ln><a:noFill/></a:ln>
   </wps:spPr><wps:bodyPr/></wps:wsp>
  </a:graphicData></a:graphic>
 </wp:anchor></w:drawing></w:r>
 <w:r><w:rPr><w:rFonts w:ascii="Liberation Serif"/><w:sz w:val="24"/></w:rPr><w:t>control</w:t></w:r>
 </w:p><w:sectPr><w:pgSz w:w="12240" w:h="15840"/>
 <w:pgMar w:left="1440" w:right="1440" w:top="1440" w:bottom="1440"/>
 </w:sectPr></w:body></w:document>'''
    return {
        "word/document.xml": document,
        "[Content_Types].xml": '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>',
        "_rels/.rels": '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="document" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>',
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="Directory for DOCX files, samples.txt and samples.sha256")
    output = parser.parse_args().output
    output.mkdir(parents=True, exist_ok=True)
    hashes = []
    for name, color in PROBES.items():
        path = output / (name + ".docx")
        with ZipFile(path, "w") as archive:
            for part, content in package(color).items():
                info = ZipInfo(part, date_time=(1980, 1, 1, 0, 0, 0))
                info.compress_type = ZIP_DEFLATED
                info.external_attr = 0o600 << 16
                archive.writestr(info, content.encode("utf-8"))
        hashes.append(hashlib.sha256(path.read_bytes()).hexdigest() + "  " + path.name)
    (output / "samples.txt").write_text("".join(name + ".docx\n" for name in PROBES), encoding="utf-8")
    (output / "samples.sha256").write_text("\n".join(hashes) + "\n", encoding="utf-8")
    print(f"Generated {len(PROBES)} color controls in {output}")


if __name__ == "__main__":
    main()
