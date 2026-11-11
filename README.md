# Local AI Upscaler

Windows용 로컬 Real-ESRGAN x4 이미지 업스케일러입니다. WPF에서 폴더 탐색·썸네일·프리뷰를 제공하고, 별도 Python Worker가 순차 추론합니다.

## 개발 환경과 실행

검증 환경: Windows x64, .NET SDK 10.0.102, Python 3.11.9, PyTorch 2.5.1+cu124. NVIDIA GPU는 선택 사항이며 CPU에서는 FP32를 사용합니다.

```powershell
./scripts/setup_environment.ps1             # 첫 준비: 고정 의존성 및 공식 모델 다운로드
./scripts/setup_environment.ps1 -VerifyOnly # 이후: 네트워크 없이 환경·체크섬 확인
./scripts/build.ps1
./scripts/run.ps1
```

앱을 닫은 상태에서 다시 빌드하세요. 실행 파일은 `src/Upscaler.Wpf/bin/Release/net10.0-windows/Upscaler.Wpf.exe`입니다. 현재 개발 버전은 소스 트리의 `python`, `models`, `.venv`를 사용합니다. Python을 포함한 설치 패키지는 후속 배포 단계입니다.

## 사용 방법

1. 폴더 열기, 주소 입력 또는 드래그 앤 드롭으로 탐색합니다.
2. 썸네일을 선택합니다. 휠이나 +/-로 확대·축소하고, 확대 이미지 위를 드래그해 이동합니다.
3. 출력 폴더·형식·장치를 선택한 뒤 현재 이미지 또는 현재 폴더를 실행합니다.
4. 확인 후 모달에서 진행·취소를 확인합니다. 실패 파일이 있으면 목록을 표시하고 다음 파일로 진행합니다.
5. 완료 후 최근 결과 보기로 결과를 프리뷰합니다.

폴더 실행은 검색 필터와 관계없이 현재 폴더의 지원 파일 전체를 대상으로 하며 하위 폴더는 제외합니다. 시작 시 목록을 고정합니다. 이미 존재하는 `_x4` 이미지도 일반 입력 이미지로 취급하므로 반복 처리 방지를 원하면 출력 폴더를 입력 폴더와 분리하세요.

## 이미지 정책

- PNG/JPEG/BMP/TIFF/WebP 입력. 확장자와 실제 포맷이 다르면 오류 처리합니다.
- EXIF 방향을 적용한 가로·세로를 기준으로 정확히 4배 출력합니다. TIFF/WebP는 첫 프레임만 처리합니다.
- 출력은 8비트 PNG 또는 JPEG입니다. 16비트 그레이스케일은 0~65535를 0~255로 매핑합니다. 부동소수점 TIFF는 거부합니다. Pillow가 8비트로 디코딩하는 컬러 입력도 8비트로 처리합니다.
- PNG 알파는 선형 보간으로 4배 확대합니다. JPEG는 흰 배경에 합성합니다.
- EXIF/ICC 등 메타데이터를 출력에 복사하지 않습니다. 전문 색 관리 및 16비트 보존은 지원 범위가 아닙니다.
- 프리뷰는 긴 변 4096픽셀로 제한하며 원본 크기를 별도 표시합니다. 원본 파일은 변경하지 않습니다.
- Pillow의 초대형 이미지 보호 한도를 넘는 입력은 안내 후 거부합니다.

## 테스트

```powershell
./scripts/test.ps1                         # Python + C# 자동 테스트
./scripts/test.ps1 -RenderUI -UIIntegration # DPI별 렌더 + 실제 WPF 모달 통합 테스트
```

C# 테스트는 외부 테스트 프레임워크 의존성이 없는 실행형 하네스이며 실패 시 비정상 종료 코드를 반환합니다. CUDA가 없으면 하드웨어 전용 Python 테스트는 건너뜁니다.

[검증 결과](docs/development-status.md) · [프로토콜](docs/protocol.md) · [문제 해결](docs/troubleshooting.md) · [설계 결정](docs/design-decisions.md) · [라이선스](docs/third-party-notices.md)
