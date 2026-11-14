# SunTool

C# / WPF(.NET 8) 기반 포터블 윈도우 유틸리티입니다. 기능을 모듈 단위로 추가할 수 있습니다.

- 저장소: https://github.com/MiniSG/SunTool
- **다운로드: [Releases](https://github.com/MiniSG/SunTool/releases/latest) 에서 `SunTool.exe` 하나만 받으면 됩니다** (설치·.NET 불필요, Windows 10/11 64비트)
- 라이선스: [MIT](LICENSE)
- 사용 매뉴얼: [docs/SunTool_사용매뉴얼.pdf](docs/SunTool_사용매뉴얼.pdf) (GitHub에서 바로 보기) · [PPT 원본](docs/SunTool_사용매뉴얼.pptx)

## 기능

| 모듈 | 설명 |
|---|---|
| **폴더 구조 복제** | 탐색기에서 원본 폴더를 끌어다 놓으면 목적 폴더에 같은 트리 구조로 폴더를 만듭니다. 트리에서 체크한 폴더는 **바로 아래 파일만** 복사하고, 그 안의 하위 폴더 내용은 복사하지 않습니다. 파일 충돌 시 처리 방식, 확장자 필터, 숨김 파일 제외를 고를 수 있습니다. |
| **파일 이름 일괄 변경** | 폴더나 파일을 끌어다 놓고 규칙을 조합합니다(텍스트 삽입: 앞/끝/N번째/특정 문자 앞뒤, 번호 매기기, 찾아 바꾸기·정규식, 삭제, 대소문자, 서식 템플릿). 미리보기에서 충돌과 잘못된 이름을 검사하고, 마지막 작업을 되돌릴 수 있습니다. 규칙 조합은 프리셋으로 저장합니다. |
| **이벤트 로그 조회** | Windows 이벤트 뷰어의 시스템·응용 프로그램·보안·설치 로그에서 **위험·오류·경고**(선택 시 정보도) 이벤트만 원하는 기간으로 모아 봅니다. 기본 기간은 최근 한 달이고 오늘·1주·1개월·3개월 버튼을 제공합니다. 수준별 개수 칩(클릭 시 해당 수준만 표시), 결과 내 검색, 상세 보기·복사, CSV(엑셀용) 저장을 지원합니다. 보안 로그는 관리자 권한으로 실행해야 읽힙니다.<br>**이벤트 ID 검색** 탭: 인터넷 없이 Windows에 등록된 이벤트 정의(원본 약 1,200개, 정의 약 11만 개)에서 ID·원본으로 메시지 형식·수준·작업 범주를 찾습니다. 처음 한 번 목록을 만들어(약 5초) `cache\event-definitions.json.gz`에 저장하고, Windows 버전·언어가 바뀌면 다시 만듭니다. 로그 목록에서 [이 ID 정의 보기]로 바로 이동할 수 있습니다. |
| **시스템 정보** | 열면 자동으로 수집합니다(약 1초 이내). **컴퓨터**(OS·보드/BIOS/시스템 시리얼·CPU·메모리 슬롯·디스크·그래픽), **통신**(랜카드별 MAC·IP·DHCP·링크 속도·수신 버퍼/점보 프레임, 메인보드 내장 외에 **추가된 COM 포트**: USB 변환·PCI 카드 구분, 기타 IO·통신 보드), **카메라·그랩보드**(장치 관리자의 장치·드라이버, Basler pylon·Teledyne DALSA Sapera LT/Xtium 우선, GenTL 생산자), **이미지 라이브러리**(HALCON 버전 등), **개발 도구**(Visual Studio 에디션·버전(vswhere), .NET, VC++ 재배포)를 보여 주고 **CSV로 저장**합니다. 감지 목록은 `SunTool.software.json`(exe 옆, [감지 목록 편집] 버튼)으로 코드 수정 없이 추가할 수 있습니다. 항목의 `category`로 표시할 화면을 고릅니다: `computer`(컴퓨터), `communication`(통신), `camera`(카메라·그랩보드), `vision`(이미지 라이브러리), `devtools`(개발 도구). 파일에 형식 오류나 잘못된 정규식이 있으면 요약 화면 맨 위에 줄 번호와 이유가 표시됩니다. 랜카드마다 [어댑터 속성 열기] 버튼으로 Windows 속성 창을 열어 IP를 바꿀 수 있습니다. |
| **통신 테스트** | **시리얼(COM)·TCP 클라이언트·TCP 서버·UDP**로 데이터를 보내고 받습니다. COM 포트는 **[연결]을 누를 때만** 열고, 프로그램을 켤 때 자동으로 열지 않습니다. 포트 목록에 장치 이름(예: USB 직렬 장치)을 함께 보여 주고, 연결 중에 장치를 뽑으면 연결을 끊습니다.<br>**로그:** 텍스트·HEX·둘 다 보기, 제어 문자는 `⟨STX⟩`처럼 표시, 줄마다 시각과 이전 줄과의 간격, 선택한 줄의 16바이트 HEX 덤프. 받은 데이터를 한 줄로 묶는 기준은 **시간 간격·끝 문자·시작·끝 문자·고정 길이** 중에서 고릅니다. 검색어는 텍스트와 HEX 모두에서 **하이라이트**하고, 일치 개수, 이전·다음(`F3`/`Shift+F3`), 일치하는 줄만 보기를 지원합니다.<br>**보내기:** 텍스트(`\r \n \xHH` 사용 가능) 또는 HEX, ASCII·UTF-8·EUC-KR, **SUM8·XOR·CRC-16(Modbus/CCITT)** 자동 붙이기(받은 데이터도 검사해 ✓/✗ 표시), CR/LF 붙이기, 보낼 바이트 미리보기, 보낸 기록. **반복 보내기**(간격·횟수), **파일 보내기**(txt 한 줄씩, 줄 범위·간격), **빠른 명령** 버튼.<br>**Steal 모드:** COM 포트 2개를 장비 1 ⇄ 장비 2 사이에 끼워, 오가는 데이터를 전달하며 `1 → 2` / `2 → 1`로 기록합니다. **듣기만**(전달 없이 기록만, Y 케이블용)과, 중계 중에 한쪽 장비로 **직접 끼워 넣어 보내기**를 지원합니다. |
| **메모리 감시** | 실행 중인 다른 프로그램(장비·비전 프로그램 등)을 골라 누수와 관련된 값(**커밋(Private Bytes)·커널 풀(페이징·NP)·가상 크기·핸들·GDI/USER 개체·스레드 수**)을 1초~1분 간격으로 기록합니다. 값을 읽기만 하고 대상 프로그램에 붙거나 멈추게 하지 않습니다.<br>**그래프** 6개(커밋·핸들·GDI/USER·커널 풀·가상 크기·스레드)에 현재·최고·증가량을 함께 보여 주고, 며칠치 기록도 점을 줄여 빠르게 그립니다. 측정값은 감시하는 동안 `logs\memwatch-프로그램-날짜.csv`에 **자동 저장**되고(시스템 전체 커밋과 한도도 참고로 함께 기록·표시), [CSV로 저장]도 됩니다. 프로그램이 죽었다 **같은 이름으로 다시 켜지면 이어서 감시**하고, 종료·재시작을 그래프에 표시합니다.<br>**참고용 추세 판정:** 처음 워밍업(기본 10분)을 빼고, 구간별 최저값(바닥선)이 꾸준히 오르는지로 `판정 보류 / 안정 / 증가 추세`를 보여 줍니다. 근거(시간당 증가량, 관찰 시간)와 GDI/USER 1만 개·32비트 2 GB(주소 공간) 한계까지 남은 시간도 표시합니다. 가상 크기는 32비트 프로그램만 판정합니다. 커밋·커널 풀·가상 크기는 워밍업 뒤 30분, 핸들·GDI·USER·스레드는 10분 이상 봐야 판정하며, 원인 위치는 알려 주지 않습니다. GDI/USER 수는 관리자·서비스 프로그램이면 권한 때문에 못 읽을 수 있습니다. |
| **장치 검색** | 핑(ICMP)으로 같은 네트워크의 살아 있는 장치를 찾습니다. CIDR(`192.168.0.0/24`) 또는 시작-끝 범위를 입력하면(최대 /16, 약 6.5만 개) 각 주소에 핑을 보내 응답한 장치의 **IP·응답시간(ms)·TTL·MAC 주소·제조사(OUI)·이름(역방향 DNS)**을 정렬 가능한 표로 보여 주고 **CSV로 저장**합니다. 동시 실행 수(기본 64)와 제외 IP 목록을 정할 수 있고, [중지]로 즉시 멈춥니다. 설정은 유지됩니다. **포트 검사·서비스 탐지·OS 판별은 하지 않는** `ping`·`arp -a` 수준의 LAN 목록 작성 도구이며, 대상 장치에 포트를 열거나 페이로드를 보내지 않습니다. 본인 소유이거나 검색 권한이 있는 네트워크에서만 사용하세요. |
| **디버그 터미널** | `F12` 키, 사이드바나 상태 표시줄의 "터미널" 버튼으로 엽니다. 실시간 로그를 보고 레벨 필터, 검색, 복사, 저장을 할 수 있고, WPF 바인딩 오류도 표시됩니다. 명령도 실행할 수 있습니다(`help`, `level debug`, `paths`, `env`, `modules`, `settings`, `lang`, `gc`, `testlog`, `clear`). 통신용 `comm` 명령으로 COM 포트 목록(포트는 열지 않음), 연결 상태, 텍스트↔HEX 변환, 체크섬·CRC 계산을 할 수 있습니다(예: `comm check 01 03 00 00 00 0A`). |
| **화면 언어** | 사이드바 아래 **[English] / [한국어]** 버튼으로 바꿉니다(프로그램이 다시 시작됨). 설정은 `Shell.Language`에 저장되고, `SunTool.exe --lang=en`으로도 지정할 수 있습니다. 터미널 로그는 한국어로 남습니다. |

## 버전 규칙

프로그램 버전은 **빌드한 날짜 + 그날 몇 번째 빌드인지**로 자동으로 정해집니다. 설정은 `Directory.Build.props`에 있습니다.

| | 같은 날 첫 빌드 | 같은 날 두 번째 빌드 |
|---|---|---|
| 파일 버전 (exe 속성) | `2026.11.9.1` | `2026.11.9.2` |
| 표시 버전 (제목 표시줄·보고서) | `v2026.11.09` | `v2026.11.09.2` |
| GitHub 릴리스 태그 | `v2026.11.09` | `v2026.11.09.2` |

- `publish.ps1`: 같은 날 다시 실행하면 마지막 숫자가 1씩 늘어납니다(`.build-number` 파일에 기록, git 제외).
- GitHub Actions 릴리스: 그날 이미 있는 릴리스 태그 수로 정합니다.
- `dotnet build`(개발용)는 마지막 숫자가 0입니다.

## 빌드 / 실행

[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)가 필요합니다. 개발할 때만 필요하고, 배포한 exe를 실행할 때는 필요하지 않습니다.

```powershell
dotnet build
dotnet test
dotnet run --project src/SunTool.App            # 일반 실행
dotnet run --project src/SunTool.App -- --console --trace   # 터미널 창과 상세 로그를 켜고 실행
```

### 포터블 배포 (단일 exe)

```powershell
.\publish.ps1
```

`publish\SunTool.exe` 하나만 복사하면 됩니다(self-contained, 약 60~70MB).

- exe 옆에 `SunTool.settings.json`, `logs\`, `undo\` 폴더가 생깁니다.
- exe가 있는 폴더에 쓰기 권한이 없으면 `%AppData%\SunTool`에 저장합니다.
- **관리자 권한으로 실행하지 마세요.** 관리자 권한으로 실행하면 Windows 보안 정책 때문에 일반 탐색기에서의 드래그앤드롭이 차단됩니다.

## 구조

```
src/
  SunTool.Core        UI와 무관한 공통 계층: 로깅, 설정, 모듈 계약(IToolModule), 파일 유틸리티
  SunTool.Wpf         공용 WPF 계층: 드래그앤드롭 동작(DropBehavior), 대화상자 서비스, 변환기
  SunTool.App         셸: DI 구성, 메인 창, 사이드바, 테마, 디버그 터미널
  SunTool.Modules.FolderReplicator   기능 1
  SunTool.Modules.BatchRename        기능 2
  SunTool.Modules.EventLogViewer     기능 3 (System.Diagnostics.EventLog 패키지 사용)
  SunTool.Modules.SystemInfo         기능 4 (System.Management 패키지, Definitions/software.json)
  SunTool.Modules.Comm               기능 5 (System.IO.Ports, System.Management, 코드 페이지 인코딩 패키지)
  SunTool.Modules.MemoryWatch        기능 6 (Process, GetGuiResources; 그래프는 WPF 로 직접 그림)
  SunTool.Modules.NetScan            기능 7 (Ping/ICMP 장치 검색, SendARP·OUI·역방향 DNS)
tests/
  SunTool.Tests       xUnit: 규칙 엔진, 충돌 검사, 복제 계획, 실제 파일 복사/이름 변경, 통신(HEX·CRC·묶기·중계·TCP/UDP 루프백), 메모리 감시(추세 판정·점 줄이기·CSV·프로세스 종료/재시작), 장치 검색(IP 범위 파싱·OUI·ARP 파싱·CSV·숫자 정렬)
```

MVVM 원칙:
- ViewModel은 `CommunityToolkit.Mvvm`(`[ObservableProperty]`, `[RelayCommand]`)을 씁니다. 창이나 MessageBox는 `IDialogService`를 통해서만 띄웁니다.
- 각 모듈은 `Logic/`(순수 로직, 테스트 대상), `ViewModels/`, `Views/` 폴더로 나눕니다.
- View는 이름 규칙으로 자동 연결됩니다: `X.ViewModels.FooViewModel` → `X.Views.FooView`.

## 새 기능(모듈) 추가하기

1. `src/SunTool.Modules.MyTool` 프로젝트를 만듭니다. `net8.0-windows`, `UseWPF`로 설정하고 Core와 Wpf를 참조합니다.
2. `IToolModule`을 구현합니다. `RegisterServices`에서 ViewModel을 등록하고, `CreateViewModel`에서 그 ViewModel을 반환합니다.
3. `ViewModels/MyToolViewModel.cs`와 `Views/MyToolView.xaml`을 만듭니다.
4. App 프로젝트에서 이 프로젝트를 참조하고, `ModuleAssemblies.cs`에 `typeof(MyToolModule).Assembly`를 한 줄 추가합니다.
5. 필요하면 `services.AddSingleton<IConsoleCommand, MyCommand>()`로 터미널 명령도 추가할 수 있습니다.

## 이름 변경 규칙 추가하기

1. `Logic/Rules/`에 `RenameRuleBase`를 상속한 클래스를 만듭니다.
2. `RenameRuleBase`에 `[JsonDerivedType(typeof(MyRule), "my")]`를 추가합니다. 프리셋 저장에 필요합니다.
3. `RuleCatalog.All`에 항목을 추가합니다.
4. `BatchRenameView.xaml`의 Resources에 `DataType="{x:Type rules:MyRule}"` 편집 템플릿을 추가합니다.

## 소스 받기 / 기여

```powershell
git clone https://github.com/MiniSG/SunTool.git
cd SunTool
dotnet build
```

버그나 감지되지 않는 SDK는 GitHub Issues에 남겨 주세요. 시스템 정보 감지 문제라면 F12 터미널의 `programs <이름>` 결과를 함께 붙이면 빠르게 확인할 수 있습니다.

## 새 버전 배포

날짜 형식 태그를 올리면 GitHub Actions(`.github/workflows/release.yml`)가 테스트 → 단일 exe 빌드 → Releases 등록을 자동으로 합니다. 태그 날짜가 프로그램 버전이 됩니다.

```powershell
git tag v2026.11.09
git push origin v2026.11.09
```

GitHub의 Actions 탭에서 **Release → Run workflow** 로 직접 실행하는 방법을 권장합니다. 한국 시간 기준 오늘 날짜로, 같은 날 두 번째부터는 `v2026.11.09.2`, `v2026.11.09.3` … 처럼 번호가 자동으로 붙습니다. 태그를 직접 올릴 때는 같은 형식을 쓰세요.

## 라이선스

[MIT License](LICENSE) © 2026 MiniSG. 개인·회사·상업 용도로 자유롭게 사용·수정·배포할 수 있으며, 배포할 때 저작권 표시와 라이선스 문구를 함께 포함하면 됩니다. 프로그램은 "있는 그대로" 제공되며 사용으로 생긴 문제에 대해 저작자는 책임지지 않습니다.
