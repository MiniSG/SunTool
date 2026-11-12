// SunTool 사용 매뉴얼 PPT 생성기 (pptxgenjs)
const path = require("path");
const pptxgen = require("pptxgenjs");
const { applyTheme } = require(process.env.PPTX_SKILL + "/scripts/apply_theme.js");

const OUT = process.argv[2] || path.join(__dirname, "SunTool_사용매뉴얼.pptx");
const IMG = (name) => path.join(__dirname, name);

const THEME = {
  name: "SunTool",
  headFontFace: "Malgun Gothic",
  bodyFontFace: "Malgun Gothic",
  colors: {
    dk1: "1F2430", lt1: "FFFFFF", dk2: "4B5563", lt2: "F3F4F6",
    accent1: "2F6FEB", accent2: "F2A93B", accent3: "1A7F37", accent4: "D1242F",
    accent5: "B7791F", accent6: "8B95A5", hlink: "2F6FEB", folHlink: "6B7280",
  },
};

const pres = new pptxgen();
pres.layout = "LAYOUT_WIDE"; // 13.333 x 7.5
pres.title = "SunTool 사용 매뉴얼";
pres.author = "SunTool";
pres.theme = { headFontFace: THEME.headFontFace, bodyFontFace: THEME.bodyFontFace };
const C = pres.SchemeColor;
const W = 13.333;
const ICON_FONT = "Segoe MDL2 Assets";
const MONO = "Consolas";

// ===== 레이아웃 =====
pres.defineSlideMaster({
  title: "TITLE_DARK",
  background: { color: C.text1 },
  objects: [
    { placeholder: { options: { name: "title", type: "title", x: 0.8, y: 2.35, w: 9.2, h: 1.3, fontSize: 44, bold: true, color: C.background1, align: "left", valign: "bottom", margin: 0 }, text: "" } },
    { placeholder: { options: { name: "body", type: "body", x: 0.8, y: 3.85, w: 9.2, h: 1.4, fontSize: 18, color: C.accent6, valign: "top", margin: 0 }, text: "" } },
  ],
});

pres.defineSlideMaster({
  title: "CONTENT",
  background: { color: C.background1 },
  objects: [
    { placeholder: { options: { name: "kicker", type: "body", x: 0.6, y: 0.32, w: 9, h: 0.32, fontSize: 12, bold: true, color: C.accent2, margin: 0 }, text: "" } },
    { placeholder: { options: { name: "title", type: "title", x: 0.6, y: 0.62, w: 12.1, h: 0.75, fontSize: 36, bold: true, color: C.text1, align: "left", valign: "middle", margin: 0 }, text: "" } },
    { text: { text: "SunTool 사용 매뉴얼", options: { x: 0.6, y: 7.05, w: 4, h: 0.3, fontSize: 10, color: C.accent6, margin: 0 } } },
  ],
  slideNumber: { x: 12.25, y: 7.05, w: 0.5, h: 0.3, fontSize: 10, color: C.accent6, align: "right" },
});

// ===== 도우미 =====
let objId = 0;
const nm = (p) => `${p}_${++objId}`;

function contentSlide(section, kicker, title) {
  const s = pres.addSlide({ masterName: "CONTENT", sectionTitle: section });
  s.addText(kicker, { placeholder: "kicker" });
  s.addText(title, { placeholder: "title" });
  return s;
}

function card(s, x, y, w, h, fill = C.background2) {
  s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y, w, h, rectRadius: 0.12, fill: { color: fill }, line: { color: fill }, objectName: nm("card") });
}

function iconCircle(s, x, y, d, glyph, fill = C.accent1, color = C.background1) {
  s.addShape(pres.shapes.OVAL, { x, y, w: d, h: d, fill: { color: fill }, line: { color: fill }, objectName: nm("iconbg") });
  s.addText(glyph, { x, y, w: d, h: d, fontFace: ICON_FONT, fontSize: Math.round(d * 30), color, align: "center", valign: "middle", margin: 0, isTextBox: true, objectName: nm("icon") });
}

function badge(s, x, y, n, d = 0.38, fill = C.accent2) {
  s.addShape(pres.shapes.OVAL, { x, y, w: d, h: d, fill: { color: fill }, line: { color: C.background1, width: 1.5 }, objectName: nm("badge") });
  s.addText(String(n), { x, y, w: d, h: d, fontSize: 14, bold: true, color: C.text1, align: "center", valign: "middle", margin: 0, isTextBox: true, objectName: nm("badgeNum") });
}

// 스크린샷 위 픽셀 좌표 → 슬라이드 좌표에 번호 배지
function screenshot(s, file, x, y, w, pxW, pxH, alt) {
  const h = (w * pxH) / pxW;
  s.addShape(pres.shapes.RECTANGLE, { x: x - 0.02, y: y - 0.02, w: w + 0.04, h: h + 0.04, fill: { color: "D9DDE3" }, line: { color: "D9DDE3" }, objectName: nm("frame") });
  s.addImage({ path: IMG(file), x, y, w, h, altText: alt, objectName: nm("shot") });
  return (px, py, n) => badge(s, x + (px / pxW) * w - 0.19, y + (py / pxH) * h - 0.19, n);
}

// 번호 + 제목 + 설명 줄
function step(s, x, y, w, n, title, desc, titleSize = 15) {
  badge(s, x, y + 0.02, n);
  s.addText(
    [
      { text: title, options: { bold: true, fontSize: titleSize, color: C.text1, breakLine: true } },
      { text: desc, options: { fontSize: 14, color: C.text2 } },
    ],
    { x: x + 0.52, y, w: w - 0.52, h: 0.78, valign: "top", margin: 0, paraSpaceAfter: 2, isTextBox: true, objectName: nm("step") }
  );
}

// ===== 1. 표지 =====
pres.addSection({ title: "시작하기" });
{
  const s = pres.addSlide({ masterName: "TITLE_DARK", sectionTitle: "시작하기" });
  // 해(Sun) 모티프
  s.addShape(pres.shapes.OVAL, { x: 9.6, y: -1.6, w: 5.2, h: 5.2, fill: { color: C.accent2, transparency: 10 }, line: { color: C.accent2 }, objectName: "sun_big" });
  s.addShape(pres.shapes.OVAL, { x: 10.9, y: 4.6, w: 1.3, h: 1.3, fill: { color: C.accent1 }, line: { color: C.accent1 }, objectName: "sun_small" });
  s.addImage({ path: IMG("img_icon.png"), x: 0.8, y: 1.2, w: 1.0, h: 1.0, altText: "SunTool 아이콘: 주황색 둥근 사각형 위 흰색 S", objectName: "appIcon" });
  s.addText("SunTool 사용 매뉴얼", { placeholder: "title" });
  s.addText(
    [
      { text: "폴더 구조 복제 · 파일 이름 일괄 변경 · 이벤트 로그 조회 · 시스템 정보 · 통신 테스트", options: { breakLine: true } },
      { text: "v2026.11.11  ·  2026년 11월  ·  github.com/MiniSG/SunTool", options: { fontSize: 14 } },
    ],
    { placeholder: "body" }
  );
}

// ===== 2. 개요 =====
{
  const s = contentSlide("시작하기", "개요", "SunTool은 이런 프로그램입니다");
  const items = [
    ["\uE8B7", C.accent2, "폴더 구조 복제", "트리 구조를 그대로 만들고, 체크한 폴더의 파일만 골라 복사합니다."],
    ["\uE8AC", C.accent1, "파일 이름 일괄 변경", "글자 넣기·번호 매기기·찾아 바꾸기를 조합해 한 번에 바꿉니다."],
    [String.fromCharCode(0xE7BA), C.accent4, "이벤트 로그 조회", "위험·오류·경고 로그를 기간으로 모아 보고 이벤트 ID 뜻을 찾습니다."],
    [String.fromCharCode(0xE7F8), C.accent3, "시스템 정보", "PC 사양·COM 포트·카메라 SDK·HALCON·VS를 한 번에 보고 CSV로 저장합니다."],
    [String.fromCharCode(0xE839), C.accent5, "통신 테스트", "COM·TCP·UDP로 보내고 받으며 텍스트와 HEX로 확인합니다."],
    ["\uE756", C.text1, "디버그 터미널", "F12 키로 실시간 로그를 보고 명령을 실행해 문제를 확인합니다."],
  ];
  items.forEach(([g, color, t, d], i) => {
    const x = 0.6 + i * 2.06;
    card(s, x, 1.75, 1.92, 3.4);
    iconCircle(s, x + 0.2, 2.05, 0.7, g, color);
    s.addText(t, { x: x + 0.2, y: 2.85, w: 1.6, h: 0.75, valign: "bottom", fontSize: 16, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: nm("cardTitle") });
    s.addText(d, { x: x + 0.2, y: 3.7, w: 1.6, h: 1.4, fontSize: 13, color: C.text2, valign: "top", margin: 0, isTextBox: true, objectName: nm("cardBody") });
  });
  const facts = [
    ["설치 없이 실행", "SunTool.exe 파일 하나 (.NET 포함)"],
    ["지원 환경", "Windows 10/11 64비트 · 한국어/English"],
    ["포터블 저장", "설정·로그를 exe 옆 폴더에 저장"],
  ];
  facts.forEach(([t, d], i) => {
    const x = 0.6 + i * 4.1;
    s.addText(
      [
        { text: t, options: { bold: true, fontSize: 16, color: C.accent1, breakLine: true } },
        { text: d, options: { fontSize: 14, color: C.text2 } },
      ],
      { x: x + 0.35, y: 5.5, w: 3.5, h: 0.85, margin: 0, valign: "top", isTextBox: true, objectName: nm("fact") }
    );
  });
}

// ===== 3. 시작하기 =====
{
  const s = contentSlide("시작하기", "시작하기", "실행은 3단계면 끝납니다");
  const flow = [
    ["\uE8C8", "exe 받기", "GitHub Releases 에서 SunTool.exe 를 받아 원하는 폴더(USB 가능)에 둡니다."],
    ["\uE768", "더블클릭 실행", "설치 과정 없이 바로 열립니다. 관리자 권한은 쓰지 마세요."],
    ["\uE700", "기능 선택", "왼쪽 메뉴에서 사용할 기능을 고릅니다. 마지막 기능을 기억합니다."],
  ];
  flow.forEach(([g, t, d], i) => {
    const x = 0.6 + i * 2.75;
    iconCircle(s, x + 0.75, 1.75, 0.95, g, i === 2 ? C.accent2 : C.accent1);
    badge(s, x + 0.55, 1.62, i + 1);
    s.addText(t, { x, y: 2.9, w: 2.45, h: 0.45, fontSize: 18, bold: true, color: C.text1, align: "center", margin: 0, isTextBox: true, objectName: nm("flowTitle") });
    s.addText(d, { x, y: 3.38, w: 2.45, h: 1.3, fontSize: 14, color: C.text2, align: "center", valign: "top", margin: 0, isTextBox: true, objectName: nm("flowBody") });
    if (i < 2) {
      s.addText("\uE72A", { x: x + 2.38, y: 1.97, w: 0.45, h: 0.5, fontFace: ICON_FONT, fontSize: 20, color: C.accent6, align: "center", valign: "middle", margin: 0, isTextBox: true, objectName: nm("arrow") });
    }
  });

  // 알아두세요
  card(s, 8.95, 1.6, 3.8, 4.85);
  s.addText("알아두세요", { x: 9.25, y: 1.85, w: 3.2, h: 0.45, fontSize: 18, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "noteTitle" });
  s.addText(
    [
      { text: "관리자 권한으로 실행하면", options: { bold: true, breakLine: true } },
      { text: "탐색기에서 끌어다 놓기가 막힙니다.", options: { breakLine: true } },
      { text: "처음 실행 시 파란 경고창", options: { bold: true, breakLine: true } },
      { text: "[추가 정보] → [실행] 을 누르세요 (서명 없는 exe).", options: { breakLine: true } },
      { text: "설정·기록 저장 위치", options: { bold: true, breakLine: true } },
      { text: "exe 옆 SunTool.settings.json, logs\\, undo\\", options: { breakLine: true } },
      { text: "쓰기 불가 위치면 %AppData%\\SunTool", options: {} },
    ],
    { x: 9.25, y: 2.4, w: 3.25, h: 4.1, fontSize: 14, color: C.text2, valign: "top", margin: 0, paraSpaceAfter: 6, isTextBox: true, objectName: "noteBody" }
  );

  card(s, 0.6, 5.0, 7.95, 1.45, "EEF3FE");
  iconCircle(s, 0.9, 5.42, 0.6, "\uE946", C.accent1);
  s.addText(
    [
      { text: "드래그앤드롭이 기본 입력 방식입니다", options: { bold: true, fontSize: 16, color: C.text1, breakLine: true } },
      { text: "탐색기에서 폴더나 파일을 끌어 점선 상자(드롭 영역)에 놓으면 파란색으로 강조되며 바로 읽어 들입니다. [찾아보기...] 버튼도 같은 역할을 합니다.", options: { fontSize: 14, color: C.text2 } },
    ],
    { x: 1.75, y: 5.0, w: 6.55, h: 1.45, valign: "middle", margin: 0, isTextBox: true, objectName: "dropTip" }
  );
}

// ===== 4. 화면 구성 =====
{
  const s = contentSlide("시작하기", "시작하기", "화면 구성");
  const mark = screenshot(s, "img_folder-0.png", 0.6, 1.55, 8.4, 1280, 820, "SunTool 메인 화면: 왼쪽 기능 메뉴, 오른쪽 작업 영역, 아래 상태 표시줄");
  mark(212, 152, 1);
  mark(880, 66, 2);
  mark(560, 600, 3);
  mark(30, 770, 4);
  mark(1220, 760, 5);
  const legend = [
    ["기능 메뉴", "사용할 기능을 고릅니다. 기능은 모듈로 계속 추가할 수 있습니다."],
    ["기능 이름·설명", "지금 화면이 무엇을 하는지 한 줄로 알려 줍니다."],
    ["작업 영역", "번호(①②③…) 순서대로 위에서 아래로 진행합니다."],
    ["상태 표시줄", "가장 최근 작업 결과와 경고·오류 개수를 보여 줍니다."],
    ["터미널 버튼", "로그 창을 엽니다. 단축키 F12 또는 Ctrl + `"],
  ];
  legend.forEach(([t, d], i) => step(s, 9.35, 1.6 + i * 1.03, 3.4, i + 1, t, d));
}

// ===== 5. 폴더 구조 복제 — 원리 =====
pres.addSection({ title: "폴더 구조 복제" });
{
  const s = contentSlide("폴더 구조 복제", "기능 1 · 폴더 구조 복제", "폴더는 전부, 파일은 체크한 곳만");
  const src = [
    [0, "2025_신제품", false, ""],
    [1, "개발", false, ""],
    [2, "문서", false, "파일 1"],
    [2, "소스", false, "파일 2"],
    [1, "기획", true, "파일 2"],
    [2, "회의록", true, "파일 2"],
    [1, "디자인", true, "파일 1"],
    [2, "시안", false, "파일 3"],
    [2, "최종", true, "파일 2"],
    [1, "마케팅", false, "파일 2"],
  ];
  const dst = [
    [0, "2025_신제품", ""],
    [1, "개발", ""],
    [2, "문서", "(비어 있음)"],
    [2, "소스", "(비어 있음)"],
    [1, "기획", "기획서_v3.pptx, 일정표.xlsx"],
    [2, "회의록", "0105_킥오프.docx 외 1"],
    [1, "디자인", "디자인가이드.pdf"],
    [2, "시안", "(비어 있음)"],
    [2, "최종", "로고_최종.ai, 패키지_최종.pdf"],
    [1, "마케팅", "(비어 있음)"],
  ];
  const rowH = 0.36;
  const top = 2.2;

  card(s, 0.6, 1.55, 4.6, 4.45);
  s.addText("원본  (체크 = 파일 복사)", { x: 0.85, y: 1.7, w: 4.2, h: 0.4, fontSize: 15, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "srcHead" });
  src.forEach(([lvl, name, checked, info], i) => {
    const y = top + i * rowH;
    const x = 0.85 + lvl * 0.38;
    if (checked) {
      s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y: y + 0.07, w: 0.22, h: 0.22, rectRadius: 0.04, fill: { color: C.accent1 }, line: { color: C.accent1 }, objectName: nm("chk") });
      s.addText("\uE73E", { x, y: y + 0.07, w: 0.22, h: 0.22, fontFace: ICON_FONT, fontSize: 9, color: C.background1, align: "center", valign: "middle", margin: 0, isTextBox: true, objectName: nm("chkMark") });
    } else {
      s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y: y + 0.07, w: 0.22, h: 0.22, rectRadius: 0.04, fill: { color: C.background1 }, line: { color: "9CA3AF", width: 1 }, objectName: nm("unchk") });
    }
    s.addText(
      [
        { text: "\uE8B7 ", options: { fontFace: ICON_FONT, color: C.accent2, fontSize: 12 } },
        { text: name, options: { color: C.text1, bold: checked, fontSize: 14 } },
        { text: info ? "   " + info : "", options: { color: C.accent6, fontSize: 12 } },
      ],
      { x: x + 0.3, y, w: 3.8 - lvl * 0.38, h: rowH, valign: "middle", margin: 0, isTextBox: true, objectName: nm("srcRow") }
    );
  });

  s.addText("\uE72A", { x: 5.3, y: 3.45, w: 0.7, h: 0.7, fontFace: ICON_FONT, fontSize: 28, color: C.accent2, align: "center", valign: "middle", margin: 0, isTextBox: true, objectName: "flowArrow" });

  card(s, 6.1, 1.55, 6.65, 4.45);
  s.addText("결과  S:\\백업\\2025_신제품", { x: 6.35, y: 1.7, w: 6.2, h: 0.4, fontSize: 15, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "dstHead" });
  dst.forEach(([lvl, name, files], i) => {
    const y = top + i * rowH;
    const x = 6.35 + lvl * 0.38;
    const empty = files.startsWith("(");
    s.addText(
      [
        { text: "\uE8B7 ", options: { fontFace: ICON_FONT, color: C.accent2, fontSize: 12 } },
        { text: name, options: { color: C.text1, fontSize: 14 } },
        { text: files ? "   " + files : "", options: { color: empty ? C.accent6 : C.accent3, fontSize: 12, bold: !empty } },
      ],
      { x, y, w: 6.2 - lvl * 0.38, h: rowH, valign: "middle", margin: 0, isTextBox: true, objectName: nm("dstRow") }
    );
  });

  card(s, 0.6, 6.15, 12.15, 0.75, "FFF6E5");
  s.addText(
    [
      { text: "핵심  ", options: { bold: true, color: C.accent5 } },
      { text: "체크한 폴더는 '바로 아래 파일'만 복사합니다. '디자인'을 체크해도 그 안의 '시안'·'최종' 파일은 각각 체크해야 복사됩니다.", options: { color: C.text1 } },
    ],
    { x: 0.85, y: 6.15, w: 11.7, h: 0.75, fontSize: 14, valign: "middle", margin: 0, isTextBox: true, objectName: "keyPoint" }
  );
}

// ===== 6. 폴더 구조 복제 — 사용 순서 =====
{
  const s = contentSlide("폴더 구조 복제", "기능 1 · 폴더 구조 복제", "사용 순서");
  screenshot(s, "img_folder-0.png", 0.6, 1.55, 8.0, 1280, 820, "폴더 구조 복제 화면: 원본 폴더 트리에서 기획, 회의록, 디자인, 최종 폴더가 체크되어 있고 미리 계산 결과가 표시됨");
  const steps = [
    ["원본 폴더 지정", "화면 ① · 탐색기에서 끌어다 놓거나 [찾아보기]"],
    ["목적 폴더 지정", "화면 ② · 결과를 만들 위치 (원본 안쪽은 불가)"],
    ["파일을 복사할 폴더 체크", "화면 ① 트리 · 우클릭하면 하위 폴더 일괄 선택"],
    ["옵션 확인", "화면 ③ · 같은 이름 파일, 확장자, 숨김 파일"],
    ["[미리 계산] → [실행]", "화면 ④ · 만들 폴더·파일 수를 확인하고 실행"],
  ];
  steps.forEach(([t, d], i) => step(s, 8.95, 1.6 + i * 1.03, 3.8, i + 1, t, d));
}

// ===== 7. 폴더 구조 복제 — 옵션 =====
{
  const s = contentSlide("폴더 구조 복제", "기능 1 · 폴더 구조 복제", "옵션과 유용한 기능");
  const head = { bold: true, color: C.background1, fill: { color: C.text1 }, fontSize: 14 };
  const cell = { color: C.text1, fontSize: 14 };
  const sub = { color: C.text2, fontSize: 14 };
  const rows = [
    [{ text: "옵션", options: head }, { text: "설명", options: head }],
    [{ text: "최상위 폴더 만들기", options: { ...cell, bold: true } }, { text: "켜면 목적 폴더 안에 원본 이름(예: 2025_신제품) 폴더를 만들고 그 안에 복제", options: sub }],
    [{ text: "같은 이름 파일: 건너뛰기", options: { ...cell, bold: true } }, { text: "기존 파일을 그대로 둡니다 (기본값)", options: sub }],
    [{ text: "덮어쓰기", options: cell }, { text: "항상 원본으로 바꿉니다", options: sub }],
    [{ text: "원본이 더 최신일 때만", options: cell }, { text: "수정한 시각을 비교해 새 파일만 덮어씁니다", options: sub }],
    [{ text: "둘 다 유지", options: cell }, { text: "'이름 (2).확장자' 로 따로 저장합니다", options: sub }],
    [{ text: "복사할 확장자", options: { ...cell, bold: true } }, { text: "예: jpg;png  — 비워 두면 모든 파일", options: sub }],
    [{ text: "숨김/시스템 제외", options: { ...cell, bold: true } }, { text: "숨김 폴더는 하위 폴더까지 통째로 건너뜁니다", options: sub }],
  ];
  s.addTable(rows, {
    x: 0.6, y: 1.6, w: 7.7, colW: [2.75, 4.95], rowH: 0.56,
    border: { type: "solid", pt: 0.75, color: "D9DDE3" }, valign: "middle", margin: [0.04, 0.12, 0.04, 0.12],
    objectName: "optionTable",
  });

  card(s, 8.65, 1.6, 4.1, 5.25);
  s.addText("이런 것도 됩니다", { x: 8.95, y: 1.82, w: 3.5, h: 0.42, fontSize: 18, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "tipsTitle" });
  const tips = [
    ["\uE8FD", "폴더 우클릭 메뉴", "하위 폴더 모두 선택·해제, 바로 아래만 선택"],
    ["\uE72C", "[다시 읽기]", "폴더 내용이 바뀌어도 체크 상태를 유지"],
    ["\uE711", "[취소]", "복사 도중에도 안전하게 멈춤"],
    ["\uE838", "[목적 폴더 열기]", "결과를 탐색기에서 바로 확인"],
  ];
  tips.forEach(([g, t, d], i) => {
    const y = 2.4 + i * 1.08;
    iconCircle(s, 8.95, y, 0.5, g, C.accent1);
    s.addText(
      [
        { text: t, options: { bold: true, fontSize: 15, color: C.text1, breakLine: true } },
        { text: d, options: { fontSize: 14, color: C.text2 } },
      ],
      { x: 9.6, y: y - 0.05, w: 2.95, h: 0.95, valign: "top", margin: 0, isTextBox: true, objectName: nm("tip") }
    );
  });
}

// ===== 8. 파일 이름 일괄 변경 — 사용 순서 =====
pres.addSection({ title: "파일 이름 일괄 변경" });
{
  const s = contentSlide("파일 이름 일괄 변경", "기능 2 · 파일 이름 일괄 변경", "사용 순서");
  screenshot(s, "img_rename-0.png", 0.6, 1.55, 8.0, 1280, 820, "파일 이름 일괄 변경 화면: IMG_ 로 시작하는 사진 8장이 제주여행_001_2025.jpg 형식으로 미리보기됨");
  const steps = [
    ["파일 추가", "화면 ① · 폴더·파일을 끌어다 놓기 (여러 번)"],
    ["대상과 정렬 선택", "하위 폴더·확장자, 정렬 = 번호 순서"],
    ["규칙 추가", "화면 ② · [+ 규칙], 위에서부터 차례로 적용"],
    ["규칙 값 입력", "아래 설정 칸 · 입력 즉시 미리보기 반영"],
    ["미리보기 확인", "가운데 표 · 새 이름과 상태 확인"],
    ["[이름 바꾸기 실행]", "잘못되면 [마지막 작업 되돌리기]"],
  ];
  steps.forEach(([t, d], i) => step(s, 8.95, 1.55 + i * 0.88, 3.8, i + 1, t, d));
}

// ===== 9. 규칙 6가지 =====
{
  const s = contentSlide("파일 이름 일괄 변경", "기능 2 · 파일 이름 일괄 변경", "변환 규칙 6가지");
  const rules = [
    ["\uE8D2", "텍스트 삽입", "앞·끝·N번째 위치·특정 글자 앞뒤에 같은 글자를 넣습니다.", "보고서", "2025_보고서"],
    ["\uE8EF", "번호 매기기", "정렬 순서대로 번호. 시작·증가·자릿수·서식 지정.", "IMG_0912", "제주여행_001"],
    ["\uE8AB", "찾아 바꾸기", "문자열 또는 정규식을 찾아 다른 글자로 바꿉니다.", "회의록_final", "회의록_최종"],
    ["\uE74D", "삭제", "특정 글자 또는 위치 범위(N번째부터 M글자)를 지웁니다.", "사진_copy", "사진"],
    ["\uE8D3", "대소문자", "영문을 모두 대문자/소문자, 첫 글자만 대문자로.", "Report_FINAL", "report_final"],
    ["\uE943", "서식 템플릿", "{name} {n:000} {date} 같은 토큰으로 이름 전체를 조립.", "IMG_0912", "20250712_01"],
  ];
  rules.forEach(([g, t, d, before, after], i) => {
    const col = i % 3, row = Math.floor(i / 3);
    const x = 0.6 + col * 4.1, y = 1.55 + row * 2.72;
    card(s, x, y, 3.85, 2.5);
    iconCircle(s, x + 0.3, y + 0.3, 0.58, g, row === 0 ? C.accent1 : C.text1);
    s.addText(t, { x: x + 1.05, y: y + 0.36, w: 2.6, h: 0.46, fontSize: 18, bold: true, color: C.text1, margin: 0, valign: "middle", isTextBox: true, objectName: nm("ruleName") });
    s.addText(d, { x: x + 0.3, y: y + 1.02, w: 3.3, h: 0.8, fontSize: 14, color: C.text2, valign: "top", margin: 0, isTextBox: true, objectName: nm("ruleDesc") });
    s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: x + 0.3, y: y + 1.85, w: 3.25, h: 0.42, rectRadius: 0.08, fill: { color: C.background1 }, line: { color: "D9DDE3" }, objectName: nm("exBox") });
    s.addText(
      [
        { text: before, options: { color: C.accent6 } },
        { text: "  →  ", options: { color: C.accent2, bold: true } },
        { text: after, options: { color: C.text1, bold: true } },
      ],
      { x: x + 0.42, y: y + 1.85, w: 3.05, h: 0.42, fontSize: 13, valign: "middle", margin: 0, isTextBox: true, objectName: nm("example") }
    );
  });
}

// ===== 10. 번호 매기기 & 템플릿 =====
{
  const s = contentSlide("파일 이름 일괄 변경", "기능 2 · 파일 이름 일괄 변경", "번호 매기기와 서식 템플릿");
  s.addText("번호 매기기 설정", { x: 0.6, y: 1.55, w: 5.8, h: 0.42, fontSize: 18, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "numHead" });
  const h = { bold: true, color: C.background1, fill: { color: C.text1 }, fontSize: 14 };
  const c = { color: C.text1, fontSize: 14 };
  const m = { color: C.text1, fontSize: 14, fontFace: MONO };
  s.addTable(
    [
      [{ text: "설정", options: h }, { text: "값", options: h }, { text: "결과 (photo.jpg)", options: h }],
      [{ text: "시작 · 증가 · 자릿수", options: c }, { text: "1 · 1 · 3", options: m }, { text: "001, 002, 003 …", options: m }],
      [{ text: "번호 서식 (끝에)", options: c }, { text: "_{n}", options: m }, { text: "photo_001.jpg", options: m }],
      [{ text: "번호 서식 (앞에)", options: c }, { text: "{n}-", options: m }, { text: "001-photo.jpg", options: m }],
      [{ text: "기존 이름 지우기", options: c }, { text: "제주여행_{n}", options: m }, { text: "제주여행_001.jpg", options: m }],
      [{ text: "증가값 10", options: c }, { text: "_{n}", options: m }, { text: "photo_001, _011 …", options: m }],
    ],
    { x: 0.6, y: 2.05, w: 6.0, colW: [2.1, 1.6, 2.3], rowH: 0.5, border: { type: "solid", pt: 0.75, color: "D9DDE3" }, valign: "middle", margin: [0.04, 0.1, 0.04, 0.1], objectName: "numTable" }
  );
  card(s, 0.6, 5.25, 6.0, 1.35, "EEF3FE");
  s.addText(
    [
      { text: "번호 순서 = 정렬 기준", options: { bold: true, fontSize: 15, color: C.text1, breakLine: true } },
      { text: "이름(2 < 10 자연 정렬), 수정·만든 날짜, 크기 중에서 고르거나 ▲▼ 버튼으로 직접 순서를 정합니다. 폴더마다 번호를 새로 시작할 수도 있습니다.", options: { fontSize: 14, color: C.text2 } },
    ],
    { x: 0.85, y: 5.25, w: 5.6, h: 1.35, valign: "middle", margin: 0, isTextBox: true, objectName: "sortTip" }
  );

  s.addText("서식 템플릿 토큰", { x: 7.0, y: 1.55, w: 5.7, h: 0.42, fontSize: 18, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "tplHead" });
  const tokens = [
    ["{name}", "지금까지 바뀐 이름"],
    ["{orig}", "원래 이름 (확장자 제외)"],
    ["{n}  {n:000}", "번호 (자릿수 지정)"],
    ["{parent}", "상위 폴더 이름"],
    ["{date:yyyyMMdd}", "수정한 날짜"],
    ["{created:yyMMdd}", "만든 날짜"],
    ["{today:yyyy-MM-dd}", "오늘 날짜"],
    ["{ext}  {count}", "확장자 · 전체 파일 수"],
  ];
  s.addTable(
    [[{ text: "토큰", options: h }, { text: "의미", options: h }], ...tokens.map(([t, d]) => [{ text: t, options: m }, { text: d, options: c }])],
    { x: 7.0, y: 2.05, w: 5.75, colW: [2.6, 3.15], rowH: 0.42, border: { type: "solid", pt: 0.75, color: "D9DDE3" }, valign: "middle", margin: [0.03, 0.1, 0.03, 0.1], objectName: "tokenTable" }
  );
  card(s, 7.0, 6.0, 5.75, 0.85, "FFF6E5");
  s.addText(
    [
      { text: "{parent}_{date:yyMMdd}_{n:00}", options: { fontFace: MONO, bold: true, color: C.text1 } },
      { text: "  →  ", options: { bold: true, color: C.accent2 } },
      { text: "제주사진_250712_01", options: { bold: true, color: C.text1 } },
    ],
    { x: 7.2, y: 6.0, w: 5.45, h: 0.85, fontSize: 14, valign: "middle", margin: 0, isTextBox: true, objectName: "tplExample" }
  );
}

// ===== 11. 상태 & 안전장치 =====
{
  const s = contentSlide("파일 이름 일괄 변경", "기능 2 · 파일 이름 일괄 변경", "미리보기 상태와 안전장치");
  s.addText("미리보기의 '상태' 칸", { x: 0.6, y: 1.55, w: 5.6, h: 0.42, fontSize: 18, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "statusHead" });
  const statuses = [
    ["변경", "FFFFFF", C.text1, "이름이 바뀝니다"],
    ["변경 없음", "FFFFFF", C.accent6, "규칙을 적용해도 이름이 같음"],
    ["충돌", "FFF4E5", C.accent5, "다른 파일과 새 이름이 겹침 → 건너뜀"],
    ["이름 오류", "FDECEC", C.accent4, "\\ / : * ? \" < > | 문자, 예약어 등"],
    ["규칙 오류", "FDECEC", C.accent4, "정규식·서식이 잘못됨 → 규칙 확인"],
  ];
  statuses.forEach(([t, fill, color, d], i) => {
    const y = 2.1 + i * 0.86;
    s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: 0.6, y, w: 1.55, h: 0.6, rectRadius: 0.1, fill: { color: fill }, line: { color: "D9DDE3" }, objectName: nm("chip") });
    s.addText(t, { x: 0.6, y, w: 1.55, h: 0.6, fontSize: 14, bold: true, color, align: "center", valign: "middle", margin: 0, isTextBox: true, objectName: nm("chipText") });
    s.addText(d, { x: 2.35, y, w: 3.9, h: 0.6, fontSize: 14, color: C.text2, valign: "middle", margin: 0, isTextBox: true, objectName: nm("chipDesc") });
  });
  s.addText("충돌·오류가 있는 파일은 실행해도 건너뛰고, 나머지만 바꿉니다.", { x: 0.6, y: 6.45, w: 5.7, h: 0.4, fontSize: 14, italic: true, color: C.accent1, margin: 0, isTextBox: true, objectName: "statusNote" });

  s.addText("실수해도 안전하게", { x: 6.75, y: 1.55, w: 6.0, h: 0.42, fontSize: 18, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "safetyHead" });
  const safety = [
    ["\uE7A7", "되돌리기", "마지막 이름 변경을 기록해 두고 [마지막 작업 되돌리기]로 원래 이름으로 돌립니다."],
    ["\uE8CB", "2단계 변경", "임시 이름을 거쳐 바꾸므로 a↔b 처럼 이름을 맞바꾸거나 대소문자만 바꿔도 안전합니다."],
    ["\uE74E", "프리셋 저장", "자주 쓰는 규칙 조합을 이름 붙여 저장하고 불러옵니다. 마지막 규칙은 자동으로 기억합니다."],
  ];
  safety.forEach(([g, t, d], i) => {
    const y = 2.1 + i * 1.6;
    card(s, 6.75, y, 6.0, 1.4);
    iconCircle(s, 7.05, y + 0.35, 0.7, g, i === 0 ? C.accent2 : C.accent1, i === 0 ? C.text1 : C.background1);
    s.addText(
      [
        { text: t, options: { bold: true, fontSize: 17, color: C.text1, breakLine: true } },
        { text: d, options: { fontSize: 14, color: C.text2 } },
      ],
      { x: 8.0, y: y + 0.2, w: 4.55, h: 1.05, valign: "top", margin: 0, isTextBox: true, objectName: nm("safety") }
    );
  });
}

// ===== 이벤트 로그 조회 — 사용 순서 =====
pres.addSection({ title: "이벤트 로그 조회" });
{
  const s = contentSlide("이벤트 로그 조회", "기능 3 · 이벤트 로그 조회", "위험·오류·경고 로그만 모아 보기");
  screenshot(s, "img_eventlog-0.png", 0.6, 1.55, 8.0, 1280, 820, "이벤트 로그 조회 화면: 최근 한 달 시스템·응용 프로그램 로그에서 위험 1, 오류 184, 경고 757건이 최신순으로 표시됨");
  const steps = [
    ["기간 고르기", "기본 최근 한 달, 1주·3개월 버튼도"],
    ["수준 고르기", "위험·오류·경고가 기본, 필요하면 정보"],
    ["로그 고르기", "시스템·응용 프로그램 기본 (보안은 관리자)"],
    ["[조회]", "최신순으로 최대 3,000건 (개수 변경 가능)"],
    ["결과 좁히기", "수준 칩을 누르거나 결과 안에서 검색"],
    ["상세 보기", "내용 복사 · CSV로 저장 · [이 ID 정의 보기]"],
  ];
  steps.forEach(([t, d], i) => step(s, 8.95, 1.55 + i * 0.88, 3.8, i + 1, t, d));
}

// ===== 이벤트 로그 조회 — ID 검색 =====
{
  const s = contentSlide("이벤트 로그 조회", "기능 3 · 이벤트 로그 조회", "이벤트 ID의 뜻을 오프라인으로 찾기");
  screenshot(s, "img_eventid-0.png", 0.6, 1.55, 7.0, 1280, 820, "이벤트 ID 검색 탭: Service Control Manager 7000 의 메시지 형식 '다음 오류로 인해 %1 서비스를 시작하지 못했습니다' 가 표시됨");

  card(s, 0.6, 6.18, 7.0, 0.74, "EEF3FE");
  s.addText(
    [
      { text: "처음 한 번만  ", options: { bold: true, color: C.accent1 } },
      { text: "정의 목록(원본 1,200여 개 · 정의 11만여 개)을 약 5초에 만들어 저장하고, 다음부터는 바로 찾습니다.", options: { color: C.text1 } },
    ],
    { x: 0.8, y: 6.18, w: 6.65, h: 0.74, fontSize: 13, valign: "middle", margin: 0, isTextBox: true, objectName: "catalogNote" }
  );

  const block = (y, h, title, rows, fill) => {
    card(s, 7.95, y, 4.8, h, fill);
    s.addText(title, { x: 8.2, y: y + 0.18, w: 4.35, h: 0.4, fontSize: 17, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: nm("blockTitle") });
    rows.forEach(([label, desc], i) => {
      s.addText(
        [
          { text: label, options: { bold: true, fontSize: 14, color: C.text1, breakLine: true } },
          { text: desc, options: { fontSize: 14, color: C.text2 } },
        ],
        { x: 8.2, y: y + 0.7 + i * 0.8, w: 4.35, h: 0.72, valign: "top", margin: 0, isTextBox: true, objectName: nm("blockRow") }
      );
    });
  };
  block(1.55, 2.98, "찾는 방법", [
    ["ID만 입력", "그 ID를 쓰는 모든 원본과 메시지 형식"],
    ["원본 이름 일부도 입력", "정확히 일치하는 원본이 맨 위에"],
    ["로그 목록에서 바로", "상세 창의 [이 ID 정의 보기]"],
  ], C.background2);
  block(4.7, 2.22, "알아두세요", [
    ["같은 ID라도 원본마다 뜻이 다릅니다", "원본 이름까지 꼭 확인하세요"],
    ["원인·해결 방법은 Windows에 없습니다", "[웹에서 찾기]로 Microsoft 문서 검색"],
  ], "FFF6E5");
}

// ===== 시스템 정보 — 화면과 사용 순서 =====
pres.addSection({ title: "시스템 정보" });
{
  const s = contentSlide("시스템 정보", "기능 4 · 시스템 정보", "PC 사양과 설치 SDK를 한눈에");
  const mark = screenshot(s, "img_sysinfo-0.png", 0.6, 1.55, 8.0, 1280, 820, "시스템 정보 화면: 왼쪽에 요약·컴퓨터·통신·카메라·이미지 라이브러리·개발 도구 분류, 오른쪽에 요약 카드가 표시됨");
  mark(474, 284, 1);
  mark(1214, 703, 2);
  mark(1094, 86, 3);
  mark(1000, 86, 4);
  const steps = [
    ["화면을 열면 자동 수집", "1초 안팎 · 다시 모으려면 [새로고침]"],
    ["분류 고르기", "화면 ① · 요약~개발 도구 6가지"],
    ["상태 배지 확인", "화면 ② · 설치됨·없음·확인 필요"],
    ["[CSV로 저장]", "화면 ③ · 엑셀로 여는 보고서"],
    ["[전체 복사]", "화면 ④ · 메일·메신저에 붙여넣기"],
    ["[감지 목록 편집]", "새 SDK를 코드 없이 추가 (다음 장)"],
  ];
  steps.forEach(([t, d], i) => step(s, 8.95, 1.55 + i * 0.88, 3.8, i + 1, t, d));
}

// ===== 시스템 정보 — 무엇을 확인하나 =====
{
  const s = contentSlide("시스템 정보", "기능 4 · 시스템 정보", "분류별로 확인하는 항목");
  const h = { bold: true, color: C.background1, fill: { color: C.text1 }, fontSize: 14 };
  const k = { bold: true, color: C.text1, fontSize: 14 };
  const d = { color: C.text2, fontSize: 13 };
  const rows = [
    ["컴퓨터", "제조사·모델·시리얼, OS·빌드, 메인보드·BIOS, CPU, 메모리, 디스크, 그래픽"],
    ["통신", "LAN 어댑터(속도·IP·MAC·점보 패킷), [어댑터 속성 열기]로 IP 수정, 추가된 COM 포트"],
    ["카메라 · 그랩보드", "연결된 카메라·그랩보드 장치, Basler pylon · DALSA Sapera 등 SDK 버전, GenTL"],
    ["이미지 라이브러리", "HALCON 버전, MIL · VisionPro · OpenCV 등 설치 여부와 버전"],
    ["개발 도구", "Visual Studio 에디션·버전, .NET SDK·런타임, VC++ 재배포, Git·Python 등"],
  ];
  s.addTable(
    [[{ text: "분류", options: h }, { text: "확인하는 내용", options: h }], ...rows.map(([a, b]) => [{ text: a, options: k }, { text: b, options: d }])],
    { x: 0.6, y: 1.6, w: 7.7, colW: [2.1, 5.6], rowH: 0.72, border: { type: "solid", pt: 0.75, color: "D9DDE3" }, valign: "middle", margin: [0.04, 0.12, 0.04, 0.12], objectName: "sysTable" }
  );
  card(s, 0.6, 6.0, 7.7, 0.88, "EEF3FE");
  s.addText(
    [
      { text: "COM 포트는 열지 않습니다  ", options: { bold: true, color: C.accent1 } },
      { text: "장비가 쓰는 중인 포트를 건드리지 않도록 '있는지'만 확인합니다. 메인보드 기본 포트는 '추가된 포트'에서 뺍니다.", options: { color: C.text1 } },
    ],
    { x: 0.8, y: 6.0, w: 7.35, h: 0.88, fontSize: 13, valign: "middle", margin: 0, isTextBox: true, objectName: "comNote" }
  );

  card(s, 8.65, 1.6, 4.1, 5.28);
  s.addText("감지가 안 될 때", { x: 8.95, y: 1.82, w: 3.5, h: 0.42, fontSize: 18, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "detectHead" });
  s.addText(
    [
      { text: "'확인 필요' 로 나오면", options: { bold: true, color: C.text1, breakLine: true } },
      { text: "설치 흔적은 있지만 버전을 못 읽은 경우입니다.", options: { color: C.text2, breakLine: true } },
      { text: "실제 설치 이름 확인", options: { bold: true, color: C.text1, breakLine: true } },
      { text: "F12 터미널에서", options: { color: C.text2, breakLine: true } },
      { text: "programs pylon", options: { fontFace: MONO, color: C.accent1, bold: true, breakLine: true } },
      { text: "[감지 목록 편집]으로 추가", options: { bold: true, color: C.text1, breakLine: true } },
      { text: "exe 옆 SunTool.software.json 에 이름 패턴·환경 변수·DLL 경로를 넣으면 다음 수집부터 반영됩니다 (작성법은 다음 장).", options: { color: C.text2 } },
    ],
    { x: 8.95, y: 2.4, w: 3.55, h: 4.3, fontSize: 14, valign: "top", margin: 0, paraSpaceAfter: 5, isTextBox: true, objectName: "detectBody" }
  );
}

// ===== 시스템 정보 — 감지 목록 파일 작성법 · IP 수정 =====
{
  const s = contentSlide("시스템 정보", "기능 4 · 시스템 정보", "감지 목록 파일 작성법과 IP 수정");

  // 왼쪽 위: category 표
  const h = { bold: true, color: C.background1, fill: { color: C.text1 }, fontSize: 14 };
  const m = { fontFace: MONO, color: C.text1, fontSize: 13 };
  const d = { color: C.text2, fontSize: 13 };
  const cats = [
    ["computer", "컴퓨터 · '추가 감지 항목' 카드"],
    ["communication", "통신 · '추가 감지 항목' 카드"],
    ["camera", "카메라 · 그랩보드"],
    ["vision", "이미지 라이브러리"],
    ["devtools", "개발 도구 · '기타 개발 도구' 카드"],
  ];
  s.addText("category = 표시할 화면", { x: 0.6, y: 1.55, w: 5.6, h: 0.4, fontSize: 16, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "catHead" });
  s.addTable(
    [[{ text: "category", options: h }, { text: "표시되는 화면", options: h }], ...cats.map(([a, b]) => [{ text: a, options: m }, { text: b, options: d }])],
    { x: 0.6, y: 2.0, w: 5.6, colW: [1.95, 3.65], rowH: 0.36, border: { type: "solid", pt: 0.75, color: "D9DDE3" }, valign: "middle", margin: [0.03, 0.1, 0.03, 0.1], objectName: "catTable" }
  );

  // 오른쪽 위: 예시 JSON
  s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: 6.55, y: 1.55, w: 6.2, h: 2.65, rectRadius: 0.12, fill: { color: "0D1117" }, line: { color: "0D1117" }, objectName: "swCodeBg" });
  const code = [
    ["{", "E6EDF3"],
    ["  \"software\": [", "E6EDF3"],
    ["    { \"category\": \"vision\", \"name\": \"사내 비전 라이브러리\",", "E6EDF3"],
    ["      \"programPatterns\": [ \"^MyCompany Vision\" ] },", "E6EDF3"],
    ["    { \"category\": \"devtools\", \"name\": \"Notepad++\",", "E6EDF3"],
    ["      \"programPatterns\": [ \"^Notepad\\\\+\\\\+\" ] }", "F2A93B"],
    ["  ]", "E6EDF3"],
    ["}", "E6EDF3"],
  ];
  s.addText(
    code.map(([t, col], i) => ({ text: t, options: { color: col, breakLine: i < code.length - 1 } })),
    { x: 6.8, y: 1.7, w: 5.8, h: 2.4, fontFace: MONO, fontSize: 12, valign: "middle", margin: 0, isTextBox: true, objectName: "swCode" }
  );

  // 가운데: 주의할 점
  s.addText(
    [
      { text: "\"software\" 목록은 하나만, 항목은 { } 를 쉼표로 이어서 넣습니다.", options: { bullet: true, breakLine: true } },
      { text: "정규식에서 + . ( ) 를 글자 그대로 찾으려면 \\ 를 붙이고, JSON 에서는 \\\\ 로 두 번 씁니다.", options: { bullet: true, breakLine: true } },
      { text: "잘못 쓰면 요약 화면 맨 위에 줄 번호와 이유가 나옵니다. 잘못된 항목만 '확인 필요'로 표시됩니다.", options: { bullet: true } },
    ],
    { x: 0.6, y: 4.45, w: 12.15, h: 1.0, fontSize: 14, color: C.text2, valign: "top", margin: 0, paraSpaceAfter: 3, isTextBox: true, objectName: "swTips" }
  );

  // 아래 왼쪽: 오류 표시 화면 / 아래 오른쪽: 어댑터 속성
  screenshot(s, "img_swerror-0.png", 0.6, 5.55, 6.3, 1002, 182, "요약 화면 맨 위의 감지 목록 파일 확인 필요 카드: software 목록이 2개, Notepad++ 정규식 오류가 표시됨");
  card(s, 7.25, 5.55, 5.5, 1.2, "EEF3FE");
  s.addImage({ path: IMG("img_adapter-0.png"), x: 7.45, y: 5.7, w: 5.1, h: (5.1 * 48) / 986, altText: "네트워크 카드 제목 오른쪽의 어댑터 속성 열기 링크", objectName: "adapterShot" });
  s.addText(
    [
      { text: "IP 바꾸기  ", options: { bold: true, color: C.accent1 } },
      { text: "통신 화면의 [어댑터 속성 열기] → TCP/IPv4 더블클릭. 관리자 확인 창이 뜰 수 있고, 바꾼 뒤 [새로고침].", options: { color: C.text1 } },
    ],
    { x: 7.45, y: 6.05, w: 5.15, h: 0.65, fontSize: 13, valign: "top", margin: 0, isTextBox: true, objectName: "adapterNote" }
  );
}

// ===== 통신 테스트 — 화면과 사용 순서 =====
pres.addSection({ title: "통신 테스트" });
{
  const s = contentSlide("통신 테스트", "기능 5 · 통신 테스트", "보내고 받은 데이터를 텍스트와 HEX로");
  const mark = screenshot(s, "img_comm-0.png", 0.6, 1.55, 8.0, 1266, 806, "통신 테스트 화면: TCP 서버로 연결해 STX·ETX 로 감싼 온도·습도 명령과 Modbus 요청을 주고받은 로그. 검색어 TEMP 가 노란색으로 표시되고, CRC 검사 결과 CHK 표시와 빠른 명령 버튼이 보임");
  mark(18, 64, 1);
  mark(1180, 62, 2);
  mark(18, 150, 3);
  mark(972, 150, 4);
  mark(600, 616, 5);
  mark(18, 741, 6);
  const steps = [
    ["연결 방식 고르기", "화면 ① · COM, TCP, UDP, Steal"],
    ["[연결]", "화면 ② · COM 포트는 이때만 엽니다"],
    ["찾기", "화면 ③ · 텍스트·HEX에서 강조, F3"],
    ["받은 데이터 묶기", "화면 ④ · 시간 간격·끝 문자·길이"],
    ["보내기", "화면 ⑤ · 텍스트 또는 HEX로 입력"],
    ["빠른 명령", "화면 ⑥ · 자주 쓰는 내용을 버튼으로"],
  ];
  steps.forEach(([t, d], i) => step(s, 8.95, 1.55 + i * 0.88, 3.8, i + 1, t, d));
}

// ===== 통신 테스트 — 보내기·받기 기능 =====
{
  const s = contentSlide("통신 테스트", "기능 5 · 통신 테스트", "보내기와 받기를 돕는 기능");
  const cards = [
    [String.fromCharCode(0xE8C1), C.accent1, "수신 묶기", "조각난 데이터를 한 줄로 모읍니다. 시간 간격, 끝 문자(예: 0D 0A), 시작·끝 문자(02~03), 고정 길이 중에서 고릅니다."],
    [String.fromCharCode(0xE73E), C.accent3, "검사값 자동 처리", "SUM8 · XOR · CRC-16(Modbus/CCITT)을 보낼 때 붙이고, 받은 데이터도 검사해 CHK ✓ / ✗로 표시합니다."],
    [String.fromCharCode(0xE72C), C.accent2, "반복 보내기", "간격(ms)과 횟수를 정해 같은 내용을 계속 보냅니다. 0회는 [반복 중지]를 누를 때까지 보냅니다."],
    [String.fromCharCode(0xE8E5), C.accent5, "파일 보내기", "txt 파일을 한 줄씩 보냅니다. 줄 범위와 간격을 정하고, 빈 줄은 건너뜁니다."],
    [String.fromCharCode(0xE721), C.accent4, "검색", "텍스트·HEX·상대 주소에서 찾아 강조합니다. 일치 개수, 이전·다음, 일치하는 줄만 보기를 지원합니다."],
    [String.fromCharCode(0xE74E), C.text1, "기록 남기기", "[로그 저장]으로 시각·방향·HEX·텍스트를 파일로 저장합니다. 줄을 고르면 아래에 HEX 덤프가 나옵니다."],
  ];
  cards.forEach(([g, color, t, d], i) => {
    const col = i % 3, row = Math.floor(i / 3);
    const x = 0.6 + col * 4.1, y = 1.6 + row * 2.2;
    card(s, x, y, 3.9, 2.0);
    iconCircle(s, x + 0.25, y + 0.25, 0.55, g, color, color === C.accent2 ? C.text1 : C.background1);
    s.addText(t, { x: x + 0.95, y: y + 0.25, w: 2.8, h: 0.55, fontSize: 17, bold: true, color: C.text1, valign: "middle", margin: 0, isTextBox: true, objectName: nm("featTitle") });
    s.addText(d, { x: x + 0.25, y: y + 0.92, w: 3.45, h: 1.0, fontSize: 13, color: C.text2, valign: "top", margin: 0, isTextBox: true, objectName: nm("featBody") });
  });
  card(s, 0.6, 6.15, 12.1, 0.75, "EEF3FE");
  s.addText(
    [
      { text: "COM 포트는 [연결]을 누를 때만 엽니다  ", options: { bold: true, color: C.accent1 } },
      { text: "프로그램을 켜거나 화면을 열 때 자동으로 열지 않습니다. 연결 중에 장치를 뽑으면 연결을 끊고 로그에 남깁니다.", options: { color: C.text1 } },
    ],
    { x: 0.85, y: 6.15, w: 11.7, h: 0.75, fontSize: 14, valign: "middle", margin: 0, isTextBox: true, objectName: "comOpenNote" }
  );
}

// ===== 통신 테스트 — Steal 모드 =====
{
  const s = contentSlide("통신 테스트", "기능 5 · 통신 테스트", "Steal 모드: 두 장비 사이의 통신 엿보기");
  // 연결 그림: 장비 1 ⇄ COM a [SunTool] COM b ⇄ 장비 2
  const box = (x, label, sub, fill, color, name) => {
    s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y: 1.75, w: 2.3, h: 1.3, rectRadius: 0.12, fill: { color: fill }, line: { color: fill }, objectName: name });
    s.addText(
      [
        { text: label, options: { bold: true, fontSize: 17, color, breakLine: true } },
        { text: sub, options: { fontSize: 13, color } },
      ],
      { x, y: 1.75, w: 2.3, h: 1.3, align: "center", valign: "middle", margin: 0, isTextBox: true, objectName: name + "Text" }
    );
  };
  box(0.6, "장비 1", "예: PLC, 검사기", C.background2, C.text1, "dev1");
  box(5.5, "SunTool", "COM 2개 사용", C.text1, C.background1, "suntool");
  box(10.4, "장비 2", "예: 센서, 리더기", C.background2, C.text1, "dev2");
  const link = (x, top, bottom, name) => {
    s.addText(
      [
        { text: top, options: { bold: true, fontSize: 14, color: "534AB7", breakLine: true } },
        { text: "⇄", options: { fontSize: 24, color: C.accent6, breakLine: true } },
        { text: bottom, options: { bold: true, fontSize: 14, color: "0F6E56" } },
      ],
      { x, y: 1.65, w: 2.4, h: 1.5, align: "center", valign: "middle", margin: 0, isTextBox: true, objectName: name }
    );
  };
  link(3.0, "1 → 2", "2 → 1", "link1");
  link(7.9, "1 → 2", "2 → 1", "link2");
  s.addText("장비 1 쪽 COM", { x: 2.95, y: 3.15, w: 2.5, h: 0.35, fontSize: 12, color: C.accent6, align: "center", margin: 0, isTextBox: true, objectName: "com1Label" });
  s.addText("장비 2 쪽 COM", { x: 7.85, y: 3.15, w: 2.5, h: 0.35, fontSize: 12, color: C.accent6, align: "center", margin: 0, isTextBox: true, objectName: "com2Label" });

  screenshot(s, "img_relay-0.png", 0.6, 3.75, 7.6, 1266, 150, "Steal 연결 설정: 장비 1 쪽 COM 포트, 장비 2 쪽 COM 포트, 듣기만 체크 상자, 통신 속도 설정");
  s.addText(
    [
      { text: "통신 설정(속도·패리티 등)은 양쪽에 똑같이 적용됩니다.", options: { bullet: true, breakLine: true } },
      { text: "로그 방향은 1 → 2(보라), 2 → 1(초록)으로 나뉘고 따로 걸러 볼 수 있습니다.", options: { bullet: true, breakLine: true } },
      { text: "한쪽 장치가 빠지면 중계를 멈추고, 같은 포트를 양쪽에 고르면 막습니다.", options: { bullet: true } },
    ],
    { x: 0.6, y: 4.85, w: 7.6, h: 1.6, fontSize: 14, color: C.text2, valign: "top", margin: 0, paraSpaceAfter: 6, isTextBox: true, objectName: "relayNotes" }
  );

  card(s, 8.6, 3.75, 4.1, 2.7);
  s.addText(
    [
      { text: "듣기만", options: { bold: true, fontSize: 16, color: C.text1, breakLine: true } },
      { text: "반대쪽으로 전달하지 않고 기록만 합니다. Y 케이블로 선로를 엿볼 때 씁니다.", options: { fontSize: 13, color: C.text2, breakLine: true } },
      { text: " ", options: { fontSize: 8, breakLine: true } },
      { text: "직접 끼워 넣기", options: { bold: true, fontSize: 16, color: C.text1, breakLine: true } },
      { text: "중계 중에 보내기 칸 옆에서 [장비 1 로] / [장비 2 로]를 골라 시험 데이터를 보냅니다. 반복·파일·빠른 명령도 같은 쪽으로 갑니다.", options: { fontSize: 13, color: C.text2 } },
    ],
    { x: 8.85, y: 3.95, w: 3.65, h: 2.35, valign: "top", margin: 0, paraSpaceAfter: 2, isTextBox: true, objectName: "relayOptions" }
  );
}

// ===== 12. 디버그 터미널 =====
pres.addSection({ title: "디버그 터미널" });
{
  const s = contentSlide("디버그 터미널", "디버그 터미널", "문제가 생기면 F12");
  screenshot(s, "img_terminal-0.png", 0.6, 1.55, 7.4, 1000, 560, "디버그 터미널 창: help 와 paths 명령 실행 결과가 표시됨");
  s.addText(
    [
      { text: "표시·수집 레벨 필터, 검색, 일시 정지", options: { bullet: true, breakLine: true } },
      { text: "전체 복사, 파일로 저장, 로그 폴더 열기", options: { bullet: true, breakLine: true } },
      { text: "화면 바인딩 오류와 예외도 자동 기록, 오류가 나면 창이 저절로 열림", options: { bullet: true } },
    ],
    { x: 0.6, y: 5.85, w: 7.4, h: 1.05, fontSize: 14, color: C.text2, valign: "top", margin: 0, paraSpaceAfter: 4, isTextBox: true, objectName: "termFeatures" }
  );

  card(s, 8.35, 1.55, 4.4, 1.25, "EEF3FE");
  s.addText(
    [
      { text: "여는 방법", options: { bold: true, fontSize: 15, color: C.text1, breakLine: true } },
      { text: "F12 키, Ctrl + ` 키, 또는 [터미널] 버튼", options: { fontSize: 14, color: C.text2, breakLine: true } },
      { text: "시작할 때 열기: SunTool.exe --console", options: { fontSize: 14, color: C.text2 } },
    ],
    { x: 8.55, y: 1.65, w: 4.05, h: 1.05, valign: "top", margin: 0, isTextBox: true, objectName: "termOpen" }
  );
  const h = { bold: true, color: C.background1, fill: { color: C.text1 }, fontSize: 14 };
  const m = { fontFace: MONO, color: C.text1, fontSize: 13 };
  const d = { color: C.text2, fontSize: 13 };
  const cmds = [
    ["help", "명령 목록"],
    ["paths", "설정·로그·되돌리기 위치"],
    ["level debug", "수집 레벨 변경"],
    ["env", "버전·메모리 정보"],
    ["modules", "로드된 기능 모듈"],
    ["settings", "설정 파일 내용 보기"],
    ["programs 이름", "설치 이름·환경 변수 찾기"],
    ["comm ports", "COM 포트 목록 (열지 않음)"],
    ["comm check HEX", "SUM8·XOR·CRC-16 계산"],
    ["clear", "화면 지우기"],
  ];
  s.addTable(
    [[{ text: "명령", options: h }, { text: "하는 일", options: h }], ...cmds.map(([a, b]) => [{ text: a, options: m }, { text: b, options: d }])],
    { x: 8.35, y: 2.95, w: 4.4, colW: [1.75, 2.65], rowH: 0.35, border: { type: "solid", pt: 0.75, color: "D9DDE3" }, valign: "middle", margin: [0.03, 0.1, 0.03, 0.1], objectName: "cmdTable" }
  );
}

// ===== 13. 빌드 환경 =====
pres.addSection({ title: "빌드 환경" });
{
  const s = contentSlide("빌드 환경", "개발자용 · 빌드 환경", "빌드 환경과 명령");
  card(s, 0.6, 1.55, 5.6, 5.3);
  s.addText("필요한 것", { x: 0.9, y: 1.78, w: 5.0, h: 0.42, fontSize: 18, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "reqHead" });
  const req = [
    ["운영체제", "Windows 10 / 11 (x64)"],
    [".NET SDK", "8.0 이상 (확인: 8.0.427)"],
    ["편집기 (선택)", "Visual Studio 2022 또는 VS Code"],
    ["UI / 패턴", "WPF + MVVM Toolkit 8.2.2"],
    ["기타 패키지", "MS DI · EventLog · Management · Ports 8.0"],
    ["테스트", "xUnit (단위 테스트 147개)"],
    ["버전 번호", "빌드한 날짜로 자동 (예: 2026.11.09)"],
    ["인터넷", "첫 빌드 때 NuGet 패키지 복원"],
  ];
  req.forEach(([k, v], i) => {
    const y = 2.3 + i * 0.55;
    s.addText(k, { x: 0.9, y, w: 1.65, h: 0.5, fontSize: 14, bold: true, color: C.accent1, valign: "middle", margin: 0, isTextBox: true, objectName: nm("reqKey") });
    s.addText(v, { x: 2.6, y, w: 3.45, h: 0.5, fontSize: 14, color: C.text1, valign: "middle", margin: 0, isTextBox: true, objectName: nm("reqVal") });
  });

  s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: 6.55, y: 1.55, w: 6.2, h: 5.3, rectRadius: 0.12, fill: { color: "0D1117" }, line: { color: "0D1117" }, objectName: "codeBg" });
  const code = [
    ["# 빌드", "8B949E"],
    ["dotnet build", "E6EDF3"],
    ["", "E6EDF3"],
    ["# 단위 테스트", "8B949E"],
    ["dotnet test", "E6EDF3"],
    ["", "E6EDF3"],
    ["# 실행 (터미널 창과 함께)", "8B949E"],
    ["dotnet run --project src/SunTool.App -- --console", "E6EDF3"],
    ["", "E6EDF3"],
    ["# 포터블 단일 exe 만들기", "8B949E"],
    [".\\publish.ps1", "F2A93B"],
    ["→ publish\\SunTool.exe (약 69 MB)", "7EE787"],
    ["", "E6EDF3"],
    ["# 아이콘 다시 만들기 (색·글꼴 변경 시)", "8B949E"],
    [".\\tools\\make-icon.ps1", "E6EDF3"],
  ];
  s.addText(
    code.map(([t, col], i) => ({ text: t || " ", options: { color: col, breakLine: i < code.length - 1 } })),
    { x: 6.85, y: 1.8, w: 5.7, h: 4.8, fontFace: MONO, fontSize: 14, valign: "top", margin: 0, isTextBox: true, objectName: "codeText" }
  );
}

// ===== 14. 프로젝트 구조 & 확장 =====
{
  const s = contentSlide("빌드 환경", "개발자용 · 빌드 환경", "프로젝트 구조와 기능 추가");
  const projects = [
    ["SunTool.Core", "로깅·설정·모듈 계약 (UI 없음)"],
    ["SunTool.Wpf", "드래그앤드롭·대화상자 공용"],
    ["SunTool.App", "메인 창·메뉴·터미널, exe 진입점"],
    ["Modules.FolderReplicator", "기능 1 폴더 구조 복제"],
    ["Modules.BatchRename", "기능 2 파일 이름 일괄 변경"],
    ["Modules.EventLogViewer", "기능 3 이벤트 로그 조회"],
    ["Modules.SystemInfo", "기능 4 시스템 정보"],
    ["Modules.Comm", "기능 5 통신 테스트"],
    ["SunTool.Tests", "규칙·충돌·감지·통신 테스트"],
  ];
  s.addText("src / tests 구성", { x: 0.6, y: 1.55, w: 6, h: 0.42, fontSize: 18, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "projHead" });
  projects.forEach(([p, d], i) => {
    const y = 2.05 + i * 0.54;
    card(s, 0.6, y, 6.1, 0.46);
    s.addText(p, { x: 0.85, y, w: 2.85, h: 0.46, fontFace: MONO, fontSize: 14, bold: true, color: i >= 3 && i <= 7 ? C.accent1 : C.text1, valign: "middle", margin: 0, isTextBox: true, objectName: nm("projName") });
    s.addText(d, { x: 3.75, y, w: 2.85, h: 0.46, fontSize: 14, color: C.text2, valign: "middle", margin: 0, isTextBox: true, objectName: nm("projDesc") });
  });

  s.addText("새 기능 추가 4단계", { x: 7.15, y: 1.55, w: 5.6, h: 0.42, fontSize: 18, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "extHead" });
  const ext = [
    ["모듈 프로젝트 만들기", "net8.0-windows, WPF, Core·Wpf 참조"],
    ["IToolModule 구현", "이름·아이콘·순서, ViewModel 등록"],
    ["ViewModel + View 작성", "FooViewModel ↔ FooView 이름 규칙으로 자동 연결"],
    ["App 에 한 줄 등록", "ModuleAssemblies.cs 에 어셈블리 추가"],
  ];
  ext.forEach(([t, d], i) => step(s, 7.15, 2.15 + i * 1.0, 5.6, i + 1, t, d));
  card(s, 7.15, 6.15, 5.6, 0.7, "FFF6E5");
  s.addText("이름 변경 규칙도 같은 방식으로 추가합니다 (README.md 참고).", { x: 7.4, y: 6.15, w: 5.2, h: 0.7, fontSize: 14, color: C.text1, valign: "middle", margin: 0, isTextBox: true, objectName: "extNote" });
}

// ===== GitHub 저장소와 라이선스 =====
pres.addSection({ title: "소스 코드" });
{
  const s = contentSlide("소스 코드", "소스 코드 · GitHub", "GitHub에서 받기와 라이선스");

  // 왼쪽: 저장소 주소 + 명령
  card(s, 0.6, 1.55, 6.3, 1.2, "EEF3FE");
  iconCircle(s, 0.88, 1.85, 0.6, String.fromCharCode(0xE774), C.accent1);
  s.addText(
    [
      { text: "공개 저장소 · 실행 파일은 Releases 의 SunTool.exe", options: { bold: true, fontSize: 13, color: C.text2, breakLine: true } },
      { text: "github.com/MiniSG/SunTool", options: { bold: true, fontSize: 22, color: C.accent1, hyperlink: { url: "https://github.com/MiniSG/SunTool" } } },
    ],
    { x: 1.7, y: 1.55, w: 5.1, h: 1.2, valign: "middle", margin: 0, isTextBox: true, objectName: "repoUrl" }
  );

  s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: 0.6, y: 2.95, w: 6.3, h: 3.9, rectRadius: 0.12, fill: { color: "0D1117" }, line: { color: "0D1117" }, objectName: "gitCodeBg" });
  const code = [
    ["# 소스 받기", "8B949E"],
    ["git clone https://github.com/MiniSG/SunTool.git", "E6EDF3"],
    ["cd SunTool", "E6EDF3"],
    ["dotnet build", "E6EDF3"],
    ["", "E6EDF3"],
    ["# 최신 코드로 갱신", "8B949E"],
    ["git pull", "E6EDF3"],
    ["", "E6EDF3"],
    ["# 새 버전 배포 (Actions → Release → Run workflow 또는 태그)", "8B949E"],
    ["git tag v2026.11.09", "E6EDF3"],
    ["git push origin v2026.11.09", "F2A93B"],
    ["", "E6EDF3"],
    ["# 고친 내용 올리기 (저장소 권한 필요)", "8B949E"],
    ["git add -A", "E6EDF3"],
    ["git commit -m \"변경 내용\"", "E6EDF3"],
    ["git push", "F2A93B"],
  ];
  s.addText(
    code.map(([t, col], i) => ({ text: t || " ", options: { color: col, breakLine: i < code.length - 1 } })),
    { x: 0.85, y: 3.12, w: 5.9, h: 3.65, fontFace: MONO, fontSize: 12, valign: "top", margin: 0, isTextBox: true, objectName: "gitCode" }
  );

  // 오른쪽: MIT 라이선스
  card(s, 7.25, 1.55, 5.5, 5.3);
  s.addText("MIT 라이선스", { x: 7.55, y: 1.78, w: 4.9, h: 0.42, fontSize: 18, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: "licHead" });
  const lic = [
    [String.fromCharCode(0xE73E), C.accent3, C.background1, "누구나 자유롭게", "개인·회사·상업 용도로 사용, 수정, 재배포 가능. 고친 코드를 공개할 의무 없음"],
    [String.fromCharCode(0xE8A5), C.accent2, C.text1, "조건은 하나", "배포할 때 저작권 표시(© 2026 MiniSG)와 라이선스 문구를 함께 포함"],
    [String.fromCharCode(0xE7BA), C.accent6, C.background1, "보증 없음", "프로그램은 있는 그대로 제공되며, 사용으로 생긴 문제는 저작자가 책임지지 않음"],
  ];
  lic.forEach(([g, fill, color, t, d], i) => {
    const y = 2.4 + i * 1.18;
    iconCircle(s, 7.55, y, 0.52, g, fill, color);
    s.addText(
      [
        { text: t, options: { bold: true, fontSize: 15, color: C.text1, breakLine: true } },
        { text: d, options: { fontSize: 13, color: C.text2 } },
      ],
      { x: 8.25, y: y - 0.05, w: 4.3, h: 1.05, valign: "top", margin: 0, isTextBox: true, objectName: nm("lic") }
    );
  });
  s.addText(
    [
      { text: "문제 제보  ", options: { bold: true, color: C.accent1 } },
      { text: "GitHub Issues에 남겨 주세요. SDK 감지 문제는 F12 터미널의 programs 결과를 함께 붙이면 빠릅니다.", options: { color: C.text2 } },
    ],
    { x: 7.55, y: 5.95, w: 4.95, h: 0.75, fontSize: 13, valign: "top", margin: 0, isTextBox: true, objectName: "issueNote" }
  );
}

// ===== 15. 문제 해결 =====
{
  pres.addSection({ title: "도움말" });
  const s = contentSlide("도움말", "도움말", "자주 묻는 질문");
  const faq = [
    ["끌어다 놓기가 안 돼요", "관리자 권한으로 실행했는지 확인하세요. 일반 더블클릭으로 다시 실행하면 됩니다."],
    ["실행할 때 파란 경고창이 떠요", "서명되지 않은 exe 라서 나오는 Windows 경고입니다. [추가 정보] → [실행]."],
    ["오류 원인을 알고 싶어요", "F12 터미널에서 빨간 줄을 확인하거나 [파일로 저장]해 전달하세요. logs 폴더에도 남습니다."],
    ["이름을 잘못 바꿨어요", "[마지막 작업 되돌리기]로 직전 작업을 원래 이름으로 되돌릴 수 있습니다."],
    ["설정을 처음으로 돌리려면", "프로그램을 닫고 exe 옆 SunTool.settings.json 을 지운 뒤 다시 실행하세요."],
    ["보안 로그가 안 보여요", "보안 로그는 관리자 권한으로 실행해야 읽힙니다. 이때는 끌어다 놓기가 막히니 필요할 때만 쓰세요."],
    ["설치한 SDK가 '없음'·'확인 필요'로 나와요", "F12 터미널에서 programs 이름 으로 실제 설치 이름을 확인해 [감지 목록 편집]에 추가하세요."],
    ["프로그램 버전은 어떻게 정해지나요", "빌드 날짜 + 그날 몇 번째 빌드인지입니다. 파일 버전 2026.11.9.2 = 11월 9일 두 번째 빌드 (표시는 v2026.11.09.2)."],
  ];
  faq.forEach(([q, a], i) => {
    const col = i % 2, row = Math.floor(i / 2);
    const x = 0.6 + col * 6.15, y = 1.55 + row * 1.34;
    card(s, x, y, 5.95, 1.22);
    iconCircle(s, x + 0.25, y + 0.22, 0.46, "\uE897", C.accent2, C.text1);
    s.addText(
      [
        { text: q, options: { bold: true, fontSize: 15, color: C.text1, breakLine: true } },
        { text: a, options: { fontSize: 13, color: C.text2 } },
      ],
      { x: x + 0.9, y: y + 0.14, w: 4.9, h: 1.0, valign: "top", margin: 0, paraSpaceAfter: 3, isTextBox: true, objectName: nm("faq") }
    );
  });
}

(async () => {
  await pres.writeFile({ fileName: OUT });
  await applyTheme(OUT, THEME);
  console.log("written:", OUT);
})();
