# KINFX Trade Copier

NinjaTrader 8에서 한 계좌의 거래를 여러 계좌(Prop Firm **시뮬레이션** 계좌와 Demo 계좌)로 복사합니다. 무료, 오픈 소스(MIT), 초기 **소스 alpha**입니다.

[中英對照](README.md) · [中文（繁體）](README.zh-HK.md) · [English](README.en.md) · [日本語](README.ja.md) · **한국어**

화면 언어는 앱과 같습니다: English, 中文（繁體）, 日本語, 한국어.

화면 표시: Leader＝리더, Follower＝팔로워, Dry run＝드라이런, Flat＝청산. 절차는 확인 문구와 같은 말을 씁니다.

> **기본 빌드에서는 거래가 비활성화되어 있습니다. Funded／실계좌: 사용 금지.** 이것은 소스 alpha입니다. **현재 소스는 수용되지 않았습니다.** 일반적인 시뮬레이션／provider 용도로 인정되지 않습니다. 선택적으로 사용하는 SIM TEST 빌드는 Simulator／Playback 및 운영자가 allow-list한 계좌로 주문을 보낼 수 있습니다. 그 능력은 안전 수용이 아닙니다.

<!-- QUICKSTART:START -->
## 사용할 수 있습니까?

| | |
|---|---|
| **Demo／시뮬레이터 계좌** | **준비되지 않음: 현재 후보는 미수용.** 가중된 과거 체크리스트의 완료도는 약 **83%**이며, 릴리스 준비 비율이 아닙니다. |
| **Prop Firm 계좌** | **준비되지 않음: 현재 후보는 미수용.** 같은 과거 체크리스트의 완료도는 약 **75%**입니다. 소스 `c9fbf2cb3005`(SIM TEST 빌드)는 2026-10-14에 실제 NinjaTrader에서, 운영자가 확인한 *시뮬레이션／평가* 계좌 11개로 실행되었습니다. 이것은 릴리스 수용도, 업체의 규정 준수 인증도 아닙니다. |
| **Funded／실계좌** | **금지.** |
<!-- QUICKSTART:END -->

## 진행도

<!-- PROGRESS:START -->
### 진행도: Demo／Prop Firm 카피

```text
Demo         █████████████████████████░░░░░  83.0%
Prop firm    ██████████████████████░░░░░░░░  74.7%
```

✅ 실제 NinjaTrader에서 검증함 · 🟡 오프라인 테스트만 · ⬜ 아직 아님. 필수 항목 ✅: Demo 10/14 · Prop 10/15.

과거 체크리스트의 증거이며, 현재 후보를 수용한 것이 아닙니다. 현재 후보는 거래용으로 수용되지 않았습니다. 안전 P0／수용 관문: issue-3, issue-4, issue-7, issue-9. Demo ready=False; Prop ready=False. funded／실계좌 수용은 없습니다.

- ✅ 핵심 복사: 진입／청산／취소／정정
- ✅ 계좌별 비율＋최대 수량
- ✅ 무포지션 팔로워는 잠그지 않음
- ✅ 계좌별 청산 버튼
- ✅ 선택／전체 Flatten
- ✅ TP／SL 미러링＋OCO
- ✅ 위험 시 자동 잠금, 자동 재시도 없음
- ✅ 연결 끊김／재연결 안전
- 🟡 부분 체결 처리
- 🟡 수량 변경 전 체결 일치 (#2)
- 🟡 한 계좌의 정체가 다른 계좌를 멈추지 않음 (#4)
- ✅ 실제 NT에서 10–30 계좌
- ✅ 대시보드（표, 일괄 편집, 잠금 사유） _（선택）_
- ✅ 계좌별 일일 목표／손실 한도
- ⬜ 괴리 시 한 번 Flatten (#12) _（선택）_
- ⬜ 기존 포지션 인수 (#11) _（선택）_
- ⬜ 계좌별 자체 TP/SL (#10) _（선택）_
- ⬜ 재시작 복구 (#6, 보류) _（선택）_
- ⬜ 설치 프로그램＋검토 후 릴리스
- ⬜ Prop Firm 규칙 확인（본 소프트웨어는 인증하지 않음）

지금까지의 실제 테스트: 시뮬레이터 리더 1개, 최대 11개의 **시뮬레이션** Prop Firm 평가 계좌(Lucid 5개, Tradeify 6개), MNQ, 시장가＋지정가＋역지정가, TP/SL OCO. funded 또는 실계좌는 사용한 적이 없습니다. 각 항목과 제한: [`docs/PROGRESS.json`](docs/PROGRESS.json). 배점은 가중치: ✅ 100%, 🟡 50%, 설계만 10%. 스냅샷 2026-10-15, 소스 `bfc8a2531acf`.
<!-- PROGRESS:END -->

체크와 백분율은 **가중된 과거 체크리스트**이며, **릴리스 준비 비율이 아닙니다**. 최근 실제 NinjaTrader 실행(2026-10-15)은 소스 `bfc8a2531acf`(SIM TEST 빌드)입니다. 테스트 증거이며, 현재의 정확한 소스에 대한 수용이 아닙니다. [항목별 증거와 제한](docs/PROGRESS.json) · [과거 NT 테스트 기록](docs/VM-PROGRESS.md) · [현재 오프라인 결과와 안전 차단](docs/DEVELOPERS.md).

## 설치 (Windows ＋ NinjaTrader 8. 검토된 테스트 후보만)

**참고용이며, 일반 공개 사용 안내가 아닙니다.** 컴파일되었다는 이유만으로 현재의 미수용 후보를 설치하거나 거래에 사용하지 마십시오. 먼저 정확한 소스와 안전 관문을 검토하십시오.

1. **다운로드.** 이 페이지에서 *Code > Download ZIP*을 풀어 둡니다. (또는 `git clone https://github.com/Kinfxhk/KINFXTradeCopier`.)
2. **설치.** 푼 폴더에서 PowerShell을 실행합니다:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install-simtest.ps1
   ```
   SIM TEST 빌드를 `Documents\NinjaTrader 8\bin\Custom\AddOns`에 복사하고, 덮어쓰기 전에 백업합니다. 계좌는 건드리지 않습니다. `-WhatIf`는 미리보기, `-Remove`는 제거입니다.
3. **컴파일.** NinjaTrader에서 *New > NinjaScript Editor*를 열고 **F5**를 누른 뒤 *Compile succeeded*를 기다립니다. 오류가 나면 중단합니다.
4. **계좌는 자동으로 불러옵니다.** copier를 열면 모든 NinjaTrader 계좌가 나열되며 체크할 수 있습니다. 계좌 이름을 직접 입력하지 않습니다. Simulator／Playback이 아니고 copier가 아직 시뮬레이션으로 확인하지 않은 계좌(예: Prop Firm 평가 계좌)에 대해 **Start** 또는 **Flatten**을 처음 누르면, **그 계좌 이름을 적은 위험 경고**가 나옵니다. KINFX는 평가 계좌와 funded／실계좌를 구별할 수 없습니다. **예** = 시뮬레이션 계좌로 확인하며, `Documents\NinjaTrader 8\templates\KINFXTradeCopier\sim-allow.txt`에 저장되고 다시 묻지 않습니다. **아니오** = 저장하지도 전송하지도 않습니다. 이 경고는 끌 수 없습니다. 다시 묻게 하려면 파일에서 해당 줄을 삭제하십시오.

   파일을 직접 작성할 수도 있습니다. **먼저 폴더를 만드십시오**: `Documents\NinjaTrader 8\templates\KINFXTradeCopier` (설치 프로그램은 **이 폴더를 만들지 않습니다**). 한 줄에 NinjaTrader Account `Name` 하나. 대소문자를 구분하며 완전히 일치해야 합니다. 로그인 이름이나 표시 별칭은 사용할 수 없습니다 (`= 별칭`은 선택):
   ```text
   # 운영자가 시뮬레이션으로 확인한 계좌만
   MYFIRM-SIM-12345 = Firm A
   MYFIRM-SIM-67890 = Firm B
   ```
   잘못된 이름은 funded／실계좌로의 주문을 허용할 수 있습니다. 파일은 운영자의 진술이며 provider 검증이 아닙니다. 직접 수정한 파일은 **정적 초기화** 때 한 번만 읽히며, 대시보드를 다시 열어도 다시 읽지 않습니다. 수정 후에는 중지 및 무포지션을 유지하고 copier 창을 닫은 다음 **NinjaTrader를 다시 시작**하십시오. **F5만 누르는 것**은 문서화된 핫 리로드를 보장하지 않습니다. (위험 경고에서의 대답은 즉시 적용됩니다.)

## 처음 사용

1. *New > KINFX Trade Copier*. 이것은 별도로 검토된 SIM TEST 후보의 리허설 목록이며, **현재의 미수용 소스를 실행하라는 허가가 아닙니다**.
2. **Leader**(예: `Sim101`)와 정확한 계약(예: `MNQ 12-26`)을 고릅니다. NinjaTrader 기본 Orders／Positions 탭에서 Leader와 선택한 모든 Follower가 **연결됨**이고, 해당 종목의 **포지션은 0이고 미체결 주문은 없습니다**. Start는 기존 포지션을 인수하거나 누락분을 메우지 않습니다.
3. Follower는 **1개만** 체크하고 Ratio는 **1**입니다. **Risk** 탭에서 **Dry run**을 체크합니다.
4. **Start**를 누르고 Leader, Follower, 계약, Ratio, `DRY RUN` 확인을 읽습니다. Leader의 작은 시뮬레이션 주문은 Logs에 `DRY RUN would send ...`를 내야 합니다. Dry run이 막는 것은 copier의 전송뿐이며, **Leader에 직접 넣는 주문은 막지 않습니다**. 중지하기 전에 **NinjaTrader 기본 화면에서 Leader의 미체결 주문을 취소하고 Leader 포지션을 청산한 뒤**, Leader와 Follower가 무포지션이고 미체결 주문이 없음을 확인합니다. 그다음 **Stop**을 누르고 세션이 분리되었는지 확인합니다. 거절되면 리허설을 멈추고 조사하며, 억지로 잠금을 풀지 마십시오.
5. **세션 연결이 해제된 뒤에만** Dry run 체크를 해제할 수 있습니다. 1계약 시뮬레이션 주문을 보내려면 정확한 소스에 대한 별도 검토와 허가, 그리고 `DRY RUN`이 없는 새 Start 확인이 필요합니다. 현재 후보는 이 관문을 통과하지 않았습니다. **중지 상태에서만 팔로워를 추가**하고, 선택한 모든 계좌가 연결되어 있고 깨끗한지 다시 확인한 뒤, 전체 범위를 담은 **새 Start 확인**을 읽습니다.

![Copy 탭 — 합성 DEMO](docs/screenshots/1-copy.png)

**스크린샷 출처:** 실제 대시보드 컨트롤을 **합성 DEMO** 모드로 그렸으며, **가상 계좌와 수치**입니다. 연결된 계좌, 엔진, 주문은 없습니다. **NinjaTrader／provider 거래 증거가 아닙니다.** [렌더링 소스](docs/screenshots/Screenshots.cs) · [Risk／Logs 갤러리와 소스](docs/DEVELOPERS.md#dashboard-gallery-and-source).

각 행에는 **Ratio, 일일 목표(Tgt), 일일 손실(DLL), ATM 청산 미러링, OFF, Flat**이 있습니다. **OFF**는 신규 진입을 막고 소유한 진입 주문의 취소를 요청합니다. 포지션과 청산 주문은 남습니다. **Flat**은 확인 후 그 계좌 하나만 청산합니다. Leader가 청산되면 Follower는 `Off`가 되며, 다시 명시적으로 체크해야 합니다.

## 제한

- **소스 alpha**이며, 일반적으로 수용된 거래 빌드가 아닙니다. NinjaTrader 8.1.8.3 실행(최근 2026-10-15, 소스 `bfc8a2531acf`)은 시뮬레이터 리더 1개, 운영자가 확인한 Prop Firm 시뮬레이션 팔로워 최대 11개, MNQ입니다. **아직 증명되지 않았습니다:** 20–30 계좌, Leader 연결 끊김, 실제 부분 체결, 정체된 provider 호출, 거부된 Flatten, 재시작 복구. 오프라인 컴파일／테스트는 이를 증명하지 않습니다.
- **Locked** 행은 NinjaTrader 기본 화면에서 조사하고 대조해야 합니다. 리스크 래치 재설정은 불확실한 엔진 ledger, 숨은 수락된 요청, 예외를 던진 Flatten의 **대조가 아닙니다**. 명시적이고 검증된 무포지션 복구 관문은 없습니다 ([#9](https://github.com/Kinfxhk/KINFXTradeCopier/issues/9), 알려진 FAIL `ExplicitManualFlatReconciliationAvailable`). 증거가 여전히 불확실하면 해제하지 않은 채 잠금을 유지하고 검토를 요청하십시오. copier는 Flatten을 재시도하지 않으며, 연결이 끊긴 뒤 누락분을 메우지 않습니다.
- 기준은 NinjaTrader 기본 Orders／Positions 탭이며, 이 창이 아닙니다. 직접 넣은 주문을 막을 수 없고, 체결을 보장하지 않습니다.
- 시험해 보려고 기본 빌드의 안전 보류를 제거하지 마십시오.

문제가 있으면 **가린** Logs(계좌 이름과 잔액 없음)를 첨부해 [issue](https://github.com/Kinfxhk/KINFXTradeCopier/issues)를 여십시오.

## 더 보기

[개발자 (English)](docs/DEVELOPERS.md) · [開發者（中文）](docs/DEVELOPERS.zh-HK.md) · [하지 말 것](docs/DO-NOT-DO.md) · [알려진 문제](docs/KNOWN-ISSUES.md) · [테스트 행렬](docs/TEST-MATRIX.md) · [로드맵](docs/ROADMAP.md) · [기여](CONTRIBUTING.md) · [보안](SECURITY.md)

개발자: 이식 가능한 검사(`python -B -m unittest discover -s tests -p 'test_*.py' -v`)는 패키징과 별도의 Python SPEC 연구를 다룹니다. SPEC은 C# 엔진 구현이 아닙니다. 이 검사는 copier, 증권사, 실제 주문을 실행하지 않습니다.

## 유지관리자 링크와 공개

유지관리자: KinFx. Prop Firm 규칙 조사, 비교표, 도구 링크, 추천 코드: **<https://linktr.ee/kinfx>**

그 페이지의 일부는 **제휴(affiliate)／추천 링크**입니다(예: 평가 코드와 파트너 가입). 사용하면 유지관리자가 수수료를 받을 수 있습니다. 이 링크는 **이 소프트웨어에 영향을 주지 않습니다**. 코드는 MIT이며, 추천이나 추적 로직이 없고, 여기서 업체나 구매를 권하지 않습니다. KinFx는 SEC／SFC 면허 자문가가 아니며, 내용은 투자 자문이 아닙니다. 평가 계좌의 대부분은 시뮬레이션입니다. 이 프로젝트는 그 페이지에 나온 업체와 제휴하지 않습니다.

## 라이선스

[MIT](LICENSE), © 2026 Kinfxhk. NinjaTrader, FlowBots／Replikanto, 어떤 증권사나 Prop Firm와도 제휴하지 않습니다. 경쟁 코드, 공급업체 DLL, 개인 계좌 로그, 자격 증명은 포함하지 않습니다. 소스의 Prop 템플릿은 과거의 편집 가능한 메타데이터이며, 현재 규칙이나 규정 준수 인증이 아닙니다.
