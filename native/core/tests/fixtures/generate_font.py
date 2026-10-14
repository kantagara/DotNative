"""Generate original test outlines; requires fontTools. No external font assets."""
from pathlib import Path
from tempfile import TemporaryDirectory
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.designspaceLib import DesignSpaceDocument, AxisDescriptor, SourceDescriptor
from fontTools.varLib import build

def master(path, width, bold=False):
    fb = FontBuilder(1000, isTTF=True)
    fb.setupGlyphOrder(['.notdef', 'space', 'A'])
    fb.setupCharacterMap({32: 'space', 65: 'A'})
    glyphs = {}
    for name in ['.notdef', 'space', 'A']:
        p = TTGlyphPen(None)
        if name != 'space':
            p.moveTo((50, 0)); p.lineTo((width-50, 0)); p.lineTo((width-50, 700)); p.lineTo((50, 700)); p.closePath()
        glyphs[name] = p.glyph()
    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics({n: (width, 0) for n in glyphs})
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    style = 'Bold' if bold else 'Regular'
    fb.setupNameTable({'familyName':'DotNativeTest', 'styleName':style, 'uniqueFontIdentifier':'DotNativeTest '+style, 'fullName':'DotNativeTest '+style, 'psName':'DotNativeTest-'+style})
    fb.setupOS2(sTypoAscender=800,sTypoDescender=-200,usWinAscent=800,usWinDescent=200,usWeightClass=700 if bold else 400,fsSelection=32 if bold else 64)
    fb.font["head"].macStyle = 1 if bold else 0
    fb.setupPost(); fb.setupMaxp()
    fb.font['head'].created = fb.font['head'].modified = 2082844800
    fb.save(path)

with TemporaryDirectory() as tmp:
    ds = DesignSpaceDocument()
    axis = AxisDescriptor(); axis.name='Width'; axis.tag='wdth'; axis.minimum=100; axis.default=100; axis.maximum=200; ds.addAxis(axis)
    for width, value in [(500,100),(1000,200)]:
        path = str(Path(tmp)/f'{value}.ttf'); master(path,width)
        src=SourceDescriptor();src.path=path;src.name=f'width{value}';src.location={'Width':value}; ds.addSource(src)
    font,_,_=build(ds)
    font.recalcTimestamp=False
    font.save(Path(__file__).with_name('DotNativeTest.ttf'))

master(Path(__file__).with_name("DotNativeTest-Bold.ttf"), 750, bold=True)

from fontTools.ttLib import TTFont, TTCollection
collection = TTCollection()
collection.fonts = [TTFont(Path(__file__).with_name(name), recalcTimestamp=False) for name in ['DotNativeTest.ttf', 'DotNativeTest-Bold.ttf']]
collection.save(Path(__file__).with_name('DotNativeTest.ttc'))

# A CFF face exercises the OTC branch independently of TrueType outlines.
from fontTools.pens.t2CharStringPen import T2CharStringPen
cff = FontBuilder(1000, isTTF=False)
cff.setupGlyphOrder(['.notdef', 'A'])
cff.setupCharacterMap({65: 'A'})
chars = {}
for name in ['.notdef', 'A']:
    pen = T2CharStringPen(600, None)
    pen.moveTo((50, 0)); pen.lineTo((550, 0)); pen.lineTo((550, 700)); pen.lineTo((50, 700)); pen.closePath()
    chars[name] = pen.getCharString()
cff.setupCFF('DotNativeCff-Regular', {'FamilyName': 'DotNativeCff', 'FullName': 'DotNativeCff Regular', 'Weight': 'Regular'}, chars, {})
cff.setupHorizontalMetrics({name: (600, 0) for name in chars})
cff.setupHorizontalHeader(ascent=800, descent=-200)
cff.setupNameTable({'familyName': 'DotNativeCff', 'styleName': 'Regular', 'uniqueFontIdentifier': 'DotNativeCff Regular', 'fullName': 'DotNativeCff Regular', 'psName': 'DotNativeCff-Regular'})
cff.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200, usWeightClass=400, fsSelection=64)
cff.setupPost(); cff.setupMaxp()
cff.font['head'].created = cff.font['head'].modified = 2082844800
cff.font.recalcTimestamp = False
collection.fonts = [cff.font]
collection.save(Path(__file__).with_name('DotNativeTest.otc'))
