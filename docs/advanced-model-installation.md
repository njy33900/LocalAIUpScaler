# 고급 복원 설치·실행

프로젝트 루트의 PowerShell 7에서 실행한다. 인터넷과 대용량 저장 공간이 필요하며, 기존 `.venv`는 변경하지 않는다. 모델과 환경은 Git에 포함하지 않는다.

```powershell
./scripts/setup_advanced_models.ps1 -Engine all -DownloadModels
./scripts/verify_advanced_models.ps1 -Engine all -Smoke
./scripts/run.ps1
```

환경: `.advanced/env-diffbir`, `.advanced/env-supir`; 구성: `.advanced/models.json`; 공식 소스: `artifacts/advanced-upstream`; 가중치: `models/advanced`. 설치는 고정 source revision과 requirements-*.lock.txt를 사용한다. 기존 소스의 revision이 다르거나 수정되어 있으면 덮어쓰지 않고 중단한다.

manifest `config/advanced-models.lock.json`에는 공식 URL·고정 revision·크기·SHA256이 있다. HF 대형 파일은 게시된 LFS SHA256을 대조했고 작은 설정 파일은 Git blob ID와 SHA256을 확인했다. SUPIR Q/F는 공식 README의 Drive 폴더에서 파일명/ID를 대조해 직접 다운로드했다. 게시자가 별도 SHA256을 제공하지 않아 첫 공식 다운로드에서 계산한 hash를 manifest에 고정했다. 첫 수신 hash는 독립적인 게시자 서명이 아니라는 점을 구별한다.

환경 설치 중 패키지를 추가해야 한다면 기존 `.venv`가 아니라 해당 엔진 환경을 선택한다. torch 2.2.2+cu121 / torchvision 0.17.2+cu121 / xformers 0.0.25.post1을 사용한다. SUPIR의 원본 requirements를 Windows에 통째로 설치하지 않았다. Linux Triton과 Gradio/LLaVA는 제외하고 headless 경로에 필요한 패키지만 사용한다. xformers의 Triton 미설치 경고는 확인했으며 이 Windows 실행에서 실제 추론은 통과했다.

## 수동 경로 설정

앱의 고급 복원 탭에서 `설치 / 모델 경로 설정`으로 `.advanced/models.json`을 연다. python/upstream/assets.path에 절대 경로나 프로젝트 상대 경로를 사용할 수 있다. 파일을 다른 위치로 바꾸면 SHA256도 공식 자산과 일치해야 한다. 모델 경로 변경 후 검증 receipt는 무효가 되므로 verify 스크립트로 실제 추론을 다시 검증한다.

단일 모델 검사:

```powershell
./scripts/verify_advanced_models.ps1 -Engine diffbir_v21 -Smoke
./scripts/verify_advanced_models.ps1 -Engine supir_v0q -Smoke
./scripts/verify_advanced_models.ps1 -Engine supir_v0f -Smoke
```

기본 smoke는 합성 256×256 입력을 2× 처리한다. 성공 시 자산 fingerprint/파일 크기·mtime/검증 배율을 `.advanced/<engine>.verified.json`에 저장한다. receipt 없이 파일만 있다고 실행 가능으로 표시하지 않는다. 임의로 receipt를 만들어 검증을 건너뛰지 않는다.

## 자동 검사

```powershell
./scripts/test.ps1 -RenderUI -UIIntegration
./artifacts/dotnet/dotnet.exe run --project tests/Upscaler.Tests -c Release -- --advanced-smoke diffbir_v21
./artifacts/dotnet/dotnet.exe run --project tests/Upscaler.Tests -c Release -- --advanced-smoke supir_v0q
./artifacts/dotnet/dotnet.exe run --project tests/Upscaler.Tests -c Release -- --advanced-smoke supir_v0f
```

실모델 검사는 순차 실행한다. Ollama나 다른 CUDA 작업과 동시에 실행하지 않는다. 일반 회귀 테스트는 기존 Real-ESRGAN 환경을 사용하고, 고급 실제 추론 검사는 별도 옵션으로 분리했다.

## 장애 진단

- not_installed: manifest에 표시한 파일/환경이 없다. 설치 스크립트를 실행한다.
- invalid: SHA256 또는 업스트림 revision이 다르다. 변경 파일을 원본과 대조한다. 설치기가 수정된 사용자 코드를 자동 삭제하지 않는다.
- missing_dependencies / incompatible: 해당 전용 환경의 pip check 및 runtime_probe 결과를 확인한다.
- failed_healthcheck: 실제 smoke 결과가 없거나 환경/파일이 바뀌었다. smoke의 events.jsonl와 stderr.log를 확인한다.
- Google Drive 쿼터/인증: 설치기는 공식 Drive에 먼저 접근하며 실패 오류를 그대로 남긴다. 임의 mirror로 교체하지 않는다. 연결 가능한 공식 경로에서 재개한다.
- CUDA OOM: 이전 Worker 종료를 기다린 뒤 작은 입력·타일 모드로 다시 실행한다. CPU 자동 대체는 하지 않는다.

샘플·로그: `artifacts/advanced-smoke/<engine>/`. 가중치·Python 환경을 포함한 설치 패키지와 새 Windows PC의 재설치 검증은 별도 배포 작업이다.
