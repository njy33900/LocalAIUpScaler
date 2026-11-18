# Local AI Upscaler

Windows용 로컬 AI 이미지 업스케일러입니다. WPF에서 폴더 탐색·썸네일·프리뷰를 제공하며, 별도 Python Worker가 Real-ESRGAN으로 이미지를 가로·세로 각각 4배 확대합니다. 모델을 설치한 뒤에는 추론 중 다운로드나 원격 API 호출 없이 동작합니다.

## 주요 기능

- 범용·애니메이션·경량 Real-ESRGAN 모델 선택
- 단일 이미지 또는 현재 폴더 일괄 처리
- NVIDIA CUDA / CPU 실행, 정밀도 및 타일 크기 설정
- 경량 모델 전용 노이즈 제거 강도 조절
- 좌우·상하 프리뷰 배치, 확대·이동, 썸네일 정렬·검색
- 파일별 진행·실패 표시, 취소 및 최근 결과 확인

현재 버전은 소스 기반 개발 실행을 지원합니다. Python과 모델을 포함한 독립 설치 패키지는 아직 제공하지 않습니다. SUPIR·DiffBIR 고급 복원은 별도 `v1.3_experiment` 브랜치의 실험 기능이며 이 브랜치에는 포함하지 않습니다.

## 실행 환경

| 항목 | 기준 |
|---|---|
| OS | Windows x64 |
| .NET SDK | 10.0.102 기준, `global.json`의 `latestPatch` 정책 적용 |
| Python | 3.11, 검증 버전 3.11.9 |
| PyTorch | 2.5.1+cu124, 설치 스크립트에서 고정 |
| GPU | 선택 사항. CUDA 사용 시 해당 PyTorch 빌드와 호환되는 NVIDIA 드라이버 필요 |

CPU에서는 FP32로 실행합니다. 별도 CUDA Toolkit은 이 프로젝트의 일반 실행에 필요하지 않습니다. Python 의존성은 프로젝트의 `.venv`에 설치합니다.

## 설치와 실행

저장소 루트에서 PowerShell로 실행합니다. `dotnet`과 Python Launcher의 `py -3.11`을 사용할 수 있어야 합니다.

```powershell
# 첫 설치: 가상환경, 고정 의존성, 공식 모델 다운로드 및 SHA256 검증
./scripts/setup_environment.ps1

# 기존 설치 확인: 다운로드 없이 환경·모델 무결성 검사
./scripts/setup_environment.ps1 -VerifyOnly

# Release 빌드 및 실행
./scripts/build.ps1
./scripts/run.ps1

# 시작할 이미지 폴더 지정
./scripts/run.ps1 -Folder 'D:\Images'
```

앱을 종료한 상태에서 빌드하세요. `run.ps1`은 Release 실행 파일이 없으면 먼저 빌드합니다.

### 빌드 출력

```powershell
dotnet build LocalAIUpscaler.sln -c Debug
dotnet build LocalAIUpscaler.sln -c Release
```

| 구성 | 실행 파일 |
|---|---|
| Debug | `out/Debug/Upscaler.Wpf.exe` |
| Release | `out/Release/Upscaler.Wpf.exe` |

`Directory.Build.props`에서 출력 위치를 공통 관리합니다. 중간 산출물은 프로젝트별 `obj`에 남으며, `out`은 Git 추적 대상에서 제외합니다. 실행 파일은 저장소의 `python`, `models`, `.venv`를 사용하므로 EXE만 다른 위치에 복사하는 방식은 지원하지 않습니다.

## 사용 방법

1. 왼쪽 폴더 트리·주소 입력·드래그 앤 드롭으로 이미지 폴더를 엽니다. F5 또는 폴더 헤더의 새로고침 버튼으로 목록을 갱신합니다.
2. 썸네일을 선택해 프리뷰합니다. 휠 또는 `+`/`-`로 확대하고 이미지를 드래그해 이동합니다. 프리뷰 상단에서 좌우·상하 배치를 선택하고 구분선을 끌어 영역 크기를 조절합니다.
3. 모델·출력 폴더·PNG/JPEG 형식·장치·정밀도·타일 크기를 선택합니다. 경량 모델을 선택했을 때만 노이즈 제거 바가 나타납니다.
4. 현재 이미지 또는 현재 폴더 작업을 실행하고 확인 창에서 대상과 설정을 확인합니다.
5. 처리 창에서 진행·취소·파일별 오류를 확인합니다. 완료한 결과가 있으면 결과 폴더 열기와 최근 결과 보기를 사용할 수 있습니다.

썸네일은 이름·수정일·파일 크기로 정렬하며 오름차순·내림차순을 전환할 수 있습니다. 마지막 정렬과 프리뷰 배치는 저장됩니다. 썸네일 디코더는 최대 3개를 재사용하고 최근 256장을 메모리에 캐시합니다. 앱을 다시 실행하면 기존 창을 활성화합니다.

### 출력 규칙

- **단일 이미지:** 선택한 출력 폴더에 저장합니다.
- **폴더 작업:** 선택한 출력 폴더 아래에 `<원래 폴더명>_UpScaled` 하위 폴더를 만들어 저장합니다. 예: 입력 `D:\Images\Travel`, 출력 `D:\Results` → `D:\Results\Travel_UpScaled`.
- 파일명은 `<원본 이름>_x4.png` 또는 `.jpg`이며, 같은 이름이 있으면 `_001` 등의 번호를 추가합니다. 원본과 기존 결과를 덮어쓰지 않습니다.
- 폴더 작업은 검색 필터와 관계없이 현재 폴더의 지원 파일 전체를 대상으로 합니다. 하위 폴더는 제외하며 시작 시 대상 목록을 고정합니다.
- `_x4`가 붙은 이미지도 입력으로 취급합니다. 반복 확대를 피하려면 입력과 결과 폴더를 분리하세요.
- 개별 파일이 실패하면 다음 파일을 계속 처리합니다. 취소 시 이미 완료된 결과는 보존합니다.

## 모델과 메모리 설정

| UI 모델 | 모델 ID | 용도 |
|---|---|---|
| 범용 · x4plus | `RealESRGAN_x4plus` | 기본 범용 모델, RRDB 23블록 |
| 애니메이션 · anime 6B | `RealESRGAN_x4plus_anime_6B` | 선화·일러스트, RRDB 6블록 |
| 경량 · general x4v3 | `realesr-general-x4v3` | SRVGG 기반 경량 모델, 노이즈 제거 강도 0~1 지원 |

경량 모델의 노이즈 제거 기본값은 0.5이며 general·weak-denoise 가중치를 공식 DNI 방식으로 보간합니다. 다른 모델로 전환하면 바를 숨기고 기존 강도는 유지합니다. 모델 선택은 단일·폴더 작업 모두에 적용되며 변경 시 이전 모델을 해제하고 새 모델을 로드합니다.

```powershell
# 모델만 설치하거나 기존 파일의 SHA256 확인
./scripts/install_models.ps1
```

공식 배포처의 가중치를 사용하며 설치·로딩 시 SHA256을 확인합니다. 손상된 기존 모델을 자동으로 덮어쓰지 않습니다.

| 타일 크기 | 권장 여유 VRAM 추정치 |
|---|---|
| 128 (기본) | 2 GB 이상 |
| 256 | 3 GB 이상 |
| 512 | 6 GB 이상 |

타일 크기는 한 번에 처리하는 영역 크기이며 출력 배율은 항상 4배입니다. VRAM 안내는 범용 x4plus 기준의 참고치로 모델·정밀도·환경에 따라 달라집니다. GPU 총용량이 아닌 실행 전 여유 용량을 확인하세요. 메모리 부족 시 더 작은 타일로 제한된 횟수만큼 재시도합니다. CPU에는 VRAM이 필요하지 않지만 큰 결과 이미지를 위해 시스템 메모리가 필요합니다.

## 이미지 지원 범위

- 입력: PNG, JPEG, BMP, TIFF, WebP. 확장자와 실제 포맷이 다르면 오류 처리합니다.
- EXIF 방향을 적용한 크기를 기준으로 가로·세로 각각 4배 출력합니다. TIFF·WebP는 첫 프레임만 처리합니다.
- 출력: 8비트 PNG 또는 JPEG. 16비트 그레이스케일은 0~65535를 0~255로 변환하며, 부동소수점 TIFF는 거부합니다. Pillow가 8비트로 디코딩하는 컬러 입력도 8비트로 처리합니다.
- PNG 알파는 선형 보간으로 확대하고 JPEG는 흰 배경에 합성합니다.
- EXIF·ICC 등 메타데이터를 출력에 복사하지 않습니다. 전문 색 관리와 16비트 보존은 지원하지 않습니다.
- 프리뷰의 긴 변은 4096픽셀로 제한하고 원본 치수를 별도로 표시합니다. 원본 파일은 변경하지 않습니다.
- Pillow의 초대형 이미지 보호 한도를 넘는 입력은 안내 후 거부합니다.

## 테스트와 유지보수

```powershell
./scripts/test.ps1                          # Python + C# 회귀 테스트
./scripts/test.ps1 -RenderUI -UIIntegration  # DPI별 렌더 + 실제 WPF 통합 테스트
```

C# 테스트는 외부 테스트 프레임워크 없이 실행하는 하네스이며 실패 시 비정상 종료 코드를 반환합니다. CUDA가 없으면 하드웨어 전용 검사를 건너뜁니다. UI 통합 테스트는 창을 일시적으로 표시하며, 생성 파일은 `artifacts/tests`, `artifacts/ui` 등에 저장합니다.

가독성 리팩토링 검증에서 Python 14개·C# 22개·UI 렌더 8개·UI 통합 9개가 통과했습니다. 출력 경로 변경 후 새 `out/Release`에서 C# 22개·UI 통합 9개를 다시 통과했으며, Debug·Release 빌드는 경고·오류 없이 완료했습니다.

| 위치 | 역할 |
|---|---|
| `src/Upscaler.Core` | 설정·파일 목록·배치 처리·캐시 |
| `src/Upscaler.PythonBridge` | Python 프로세스·JSONL 통신·취소·재시작 |
| `src/Upscaler.Wpf` | 탐색·프리뷰·썸네일·진행 UI |
| `python` | 모델 로딩·타일 추론·이미지 정책 |
| `scripts` | 설치·검증·빌드·실행 |
| `tests` | Python·C#·WPF 회귀 검사 |

주석은 C# XML 문서 주석, Python docstring, PowerShell comment-based help를 사용합니다. 자세한 책임과 변경 기준은 [코드 가이드](docs/code-guide.md)를 참고하세요.

## 문제 해결과 문서

환경 문제는 `./scripts/setup_environment.ps1 -VerifyOnly`로 먼저 확인하세요. 출력 오류가 나면 저장 폴더 권한과 디스크 여유를 확인하고, 빌드 파일이 잠겼다면 실행 중인 앱을 종료하세요.

- 로그: `%LocalAppData%/LocalAIUpscaler/worker.log`
- 설정: `%LocalAppData%/LocalAIUpscaler/settings.json`
- [문제 해결](docs/troubleshooting.md) · [검증 이력](docs/development-status.md) · [통신 프로토콜](docs/protocol.md)
- [설계 결정](docs/design-decisions.md) · [배포 범위와 후속 작업](docs/deployment.md) · [외부 모델·라이선스](docs/third-party-notices.md)

앱 아이콘 원본은 `src/Upscaler.Wpf/Assets/App.svg`이며 `scripts/generate_icon.py`로 PNG·ICO를 재생성합니다. 프로젝트 라이선스는 [LICENSE](LICENSE)를 확인하세요. 외부 코드·모델에는 각각의 라이선스가 적용됩니다.
