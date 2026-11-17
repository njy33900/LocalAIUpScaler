# Advanced Restoration 구현·검증 보고서

작업: 2026-11-17~18 (Asia/Seoul), 브랜치 v1.3. 기존 Real-ESRGAN을 유지하면서 별도 고급 복원 탭과 실제 SUPIR Q/F·DiffBIR 추론을 통합했다. 모델 다운로드를 사용자에게 넘기지 않고 공식 배포처에서 직접 수행했다.

## 1. 현행 구조와 확장

WPF MainViewModel/MainWindow → PythonWorker JSONL → Python 추론이라는 기존 구조를 유지했다. UI 브라우저와 폴더 스냅샷·출력 경로·ProgressWindow·BatchRunner를 재사용한다. 고급 설정은 별도 탭이며, 일반 업스케일 설정에 섞이지 않도록 모델별 프로필을 저장한다.

## 2. 실제 검증한 모델

| 엔진 | 실제 사용 가중치 | 검증 결과 |
|---|---|---|
| DiffBIR v2.1 SR | realesrgan_s4_swinir_100k.pth + sd2.1-base-zsnr-laionaes5.ckpt + DiffBIR_v2.1.pt | SwinIR Stage 1·확산 Stage 2, CUDA/FP16, 256×256 → 512×512 저장 성공 |
| SUPIR-v0Q | 공식 Q + SDXL 0.9 VAE + CLIP-L/14 + OpenCLIP bigG | CUDA/FP16 diffusion, tiled VAE, 같은 합성 입력의 512×512 저장 성공 |
| SUPIR-v0F | 공식 F + 위와 동일한 종속 자산 | 같은 입력으로 실제 F 체크포인트 로드·512×512 저장 성공 |

세 엔진 모두 runtime state=ready, verified_scales=[2]를 확인했다. 파일 존재만으로 준비 완료를 표시하지 않는다. 각 엔진의 실제 추론 receipt 및 자산 상태를 검사하며 미검증 배율은 UI에 노출하지 않는다.

## 3. 모델 확보와 무결성

고유 자산 13개, 총 36,171,612,157 bytes(약 33.69 GiB)를 확보했다. 가상환경과 업스트림 소스 크기는 이 숫자에서 제외했다.

- 공식 SUPIR README에 연결된 Google Drive 폴더를 gdown으로 조회해 파일명/ID를 대조하고 Q/F를 직접 다운로드했다.
- Q SHA256: d7f418398bb024d0d3c779c4ee4e9f171eb072306093f5bcfb2bf096aa2738f8
- F SHA256: 9d8906255e2a2cc97b52d95d575461eca2a8971647d5e3e763291965c3a35314
- SUPIR의 게시자 SHA256은 별도 제공되지 않았다. 위 값은 공식 Drive 수신 파일에서 계산한 hash를 재설치 기준으로 고정한 것이다.
- DiffBIR 및 SDXL/CLIP은 공식 Hugging Face의 고정 revision에서 다운로드했다. LFS SHA256 또는 작은 파일의 Git blob ID를 검증하고 SHA256을 manifest에 보존했다.
- 자산별 경로·크기·URL·revision·SHA256 전체: config/advanced-models.lock.json.
- LLaVA/RAM 가중치는 현재 사용하는 캡셔닝 없는 추론 경로에 필요하지 않으므로 다운로드하지 않았다. 이 경로로 세 엔진의 실제 성공을 확인했다.

## 4. 환경과 코드 revision

- 기존 Real-ESRGAN .venv / torch 2.5.1+cu124는 변경하지 않았다.
- .advanced/env-diffbir: Python 3.11.9, torch 2.2.2+cu121, torchvision 0.17.2+cu121, xformers 0.0.25.post1, transformers 4.37.2.
- .advanced/env-supir: 별도 Python 3.11.9 환경, 같은 torch/CUDA wheel, transformers 4.28.1, open-clip-torch 2.17.1, k-diffusion 0.1.1.post1.
- 두 환경 pip check 모두 통과. 전체 실제 패키지는 requirements-diffbir.lock.txt / requirements-supir.lock.txt로 고정했다.
- DiffBIR upstream: 5c2d6c177c3f2965e6d33a32931248d242ef7b3d.
- SUPIR upstream: bda91af2000042f8bedfec8897d92917e67c1d88.
- 업스트림 추적 파일은 수정하지 않았다. 어댑터가 로컬 로더·CKPT 경로를 프로세스 안에서 연결한다.

## 5. 빌드·테스트

| 검증 | 결과 / 로그 |
|---|---|
| Phase 0 기준선 | 기존 전체 검사 통과. advanced-phase0-baseline.log |
| 최종 Release build | 경고 0 / 오류 0. artifacts/phase4-final-build.log |
| Python | 25 passed. 기존 14 + 고급 계약/상태/네트워크 차단/staging 검사 11 |
| C# | 23 passed, 0 failed, 0 skipped |
| DPI 렌더 | 일반/고급 탭 각각 8장, 합계 16 passed. 1100×700 / 1440×900, 100/125/150/200% |
| WPF UI 통합 | 10 passed, 0 failed, 0 skipped. 탭 전환·일반 모델 유지 포함 |
| 고급 실모델 IPC | DiffBIR/Q/F 각각 2 passed. 실제 추론 취소·정리, 취소 후 같은 worker에서 2회 연속 성공, 출력 충돌 방지·원본 해시 보존 |
| 최종 staging 보강 후 IPC | DiffBIR 2 passed 재확인. artifacts/phase4-final-bridge.log |
| 설치 스크립트 재실행 | setup_advanced_models.ps1 -Engine all -DownloadModels 성공. 기존 검증 파일 재사용 및 hash 재확인 |

전체 회귀 로그: artifacts/phase4-final-tests.log. 모델별 IPC 로그: phase4-advanced-bridge-diffbir.log, phase4-advanced-bridge-supirq.log, phase4-advanced-bridge-supirf.log.

실모델 결과와 이벤트/오류 로그: artifacts/advanced-smoke/<engine>/. Worker 종료 후 고급 추론 프로세스가 남아 있지 않음을 확인했다. GPU 시스템 전체 사용량은 마지막 확인에서 약 4,742 MiB였으며 다른 사용자 앱 사용량을 포함하므로 이 값을 모델 VRAM 요구치로 해석하지 않는다.

## 6. 수정·추가 파일

- Core: Domain.cs의 모델별 설정 저장과 RestorationOptions.cs의 유효 범위 검증.
- PythonBridge: AdvancedRuntime.cs의 설치/경로/준비 검사, PythonWorker.cs의 restore 요청·UTF-8·작업 UUID staging 정리.
- WPF: MainViewModel.cs/MainWindow.xaml(.cs)의 별도 탭·설정 스냅샷·엔진 종료 후 전환, RestorationViewModel.cs와 RestorationPanel.xaml(.cs)의 엔진/프리셋/옵션/설치 진단, ProgressWindow.cs의 고급 단계 표시.
- Python: advanced/catalog.py, runtime_probe.py, offline.py, worker.py, diffbir_adapter.py, supir_adapter.py, install_assets.py, download_supir.py, smoke.py.
- 설치/검증: requirements-*.txt 및 *.lock.txt, setup_advanced_models.ps1, verify_advanced_models.ps1, advanced-models.lock.json.
- 테스트: test_advanced.py, Upscaler.Tests/Program.cs.
- 문서/고지: advanced-restoration.md, advanced-model-installation.md, advanced-licenses.md, 본 보고서, licenses/advanced의 공식 코드 라이선스 원문, README, .gitignore.

## 7. 발견한 오류와 해결

- SUPIR fsspec/HF hub 버전 충돌 → 호환 fsspec 고정.
- k-diffusion의 최신 W&B/protobuf 간접 충돌 → 공식 시대와 호환되는 W&B/protobuf 고정, 불필요한 최신 telemetry 의존성 제거, pip check 통과.
- DiffBIR의 facexlib 공통 import 누락 → 전용 환경에 명시.
- 오프라인 SwinIR 로더의 state_dict/module prefix 처리 누락 → 공식 common loader와 동일하게 정규화.
- 한글 경로가 CP949 표준출력으로 기록됨 → Worker와 bridge의 UTF-8 강제 후 실제 재시험 성공.
- 테스트 실행 중 DLL 잠금으로 중간 빌드 실패 → 실행 종료 후 순차 빌드로 해결. 최종 빌드 경고/오류 0개.
- xformers의 Triton 미설치 경고는 남아 있지만 Windows CUDA 연산과 세 모델 추론은 통과했다. Linux Triton을 억지로 설치하지 않았다.

## 8. 실행 방법

```powershell
./scripts/setup_advanced_models.ps1 -Engine all -DownloadModels
./scripts/verify_advanced_models.ps1 -Engine all -Smoke
./scripts/run.ps1
```

현재 이 PC에는 이미 설치·실제 검증되어 있으므로 앱 실행 후 고급 복원 탭에서 사용할 수 있다. 모델·환경 파일은 Git에서 제외했다. 별도 자동 다운로드 및 SHA 검증 스크립트로 복원한다.

## 9. 검증 한계와 남은 항목

- 검증된 장치/배율은 CUDA/2×. CPU·1×·4×·DiffBIR denoise/face·자동 캡셔너는 이번 지원 목록에 포함하지 않았다.
- 합성 RGB 1장과 실제 IPC 연속 실행으로 확인했으며 다양한 실사 사진의 시각 품질·얼굴 동일성·광범위한 이미지 크기·다른 GPU/드라이버를 검증하지 않았다.
- 기본 프리셋으로 세 모델의 실제 추론을 검증했다. 원본 보존 지향 프리셋의 품질 우월성을 수동 비교 평가한 것은 아니다.
- 알파/EXIF/16비트 정책은 공통 디코더 및 기존 테스트를 재사용한다. 고급 세 모델 각각에 모든 이미지 포맷 조합의 end-to-end 회귀를 수행하지는 않았다.
- 실제 GPU 메모리를 고갈시키는 OOM 시험과 장시간 timeout 시험은 고급 모델에 별도로 유발하지 않았다. 오류 코드와 이미지별 프로세스 종료 경로를 구현했고 실제 취소·재시작은 검증했다.
- 오프라인 검증은 HF offline 및 Python 소켓 차단 하의 실제 추론이다. OS 방화벽/패킷 캡처 감사는 별도다.
- 자동 설치기의 재실행을 검증했지만 완전히 새 Windows PC 재설치와 배포 패키징은 미검증이다.
- 모델을 이미지마다 다시 로드하므로 큰 폴더 처리 속도 최적화는 후속 과제다.

현재 변경은 v1.3의 작업 트리에 있으며 이 작업에서 커밋/푸시하지 않았다. 기존 미추적 복구 파일을 보존했다.
