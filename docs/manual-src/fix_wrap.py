"""모든 문단에 eaLnBrk="0" 을 넣어 한글 단어가 중간에서 잘리지 않게 합니다."""
import re
import shutil
import sys
import zipfile

src = sys.argv[1]
tmp = src + ".tmp"

P_OPEN = re.compile(r"<a:p>(?!<a:pPr)")
P_SELF = re.compile(r"<a:p/>")
PPR = re.compile(r"<a:pPr(?![^>]*eaLnBrk)")

with zipfile.ZipFile(src) as zin, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
    for item in zin.infolist():
        data = zin.read(item.filename)
        if re.match(r"ppt/(slides|slideLayouts|slideMasters)/[^/]+\.xml$", item.filename):
            xml = data.decode("utf-8")
            xml = PPR.sub('<a:pPr eaLnBrk="0"', xml)
            xml = P_OPEN.sub('<a:p><a:pPr eaLnBrk="0"/>', xml)
            # 한글 단어 단위 줄바꿈은 텍스트 언어가 한국어여야 적용됩니다.
            xml = xml.replace('lang="en-US"', 'lang="ko-KR" altLang="en-US"')
            data = xml.encode("utf-8")
        zout.writestr(item, data)

shutil.move(tmp, src)
print("fixed wrap:", src)
