# Advanced Restoration

기존 Real-ESRGAN과 분리된 **고급 복원** 탭이다. 이미지 브라우저·프리뷰·선택·폴더 스냅샷·기존 진행 모달을 재사용한다. 생성형 복원은 원본에 없던 세부 사항을 만들 수 있으며 원본/얼굴 동일성을 보장하지 않는다.

## 현재 검증한 실행 범위

| 엔진 | 실제 경로 | 장치 / 배율 | 기본 샘플링 |
|---|---|---|---|
| DiffBIR v2.1 | SwinIR cleaner → SD2.1-zsnr + v2.1 IRControlNet | CUDA / 2× | EDM DPM++ 3M SDE, 10 steps, CFG 6 |
| SUPIR-v0Q | SDXL + Q checkpoint + CLIP encoders | CUDA / 2× | 50 steps, CFG 4, control scale 1 |
| SUPIR-v0F | SDXL + F checkpoint + CLIP encoders | CUDA / 2× | 50 steps, CFG 4, control scale 1 |

완료한 실제 스모크의 배율만 UI에 표시한다. 현재 CPU, DiffBIR denoise/face, LLaVA/RAM 자동 캡셔닝은 지원 UI에 노출하지 않는다. 공식 추론 코드의 captioner=none 경로를 사용해 LLaVA·RAM 가중치는 필요하지 않다. SUPIR도 LLaVA 전처리/캡션을 생략하고 빈 caption과 수동 prompt를 사용한다.

SUPIR는 짧은 변을 최소 1024, 64 배수로 조정해 내부 추론한 뒤 목표 크기로 리샘플한다. DiffBIR v2.1 SR은 입력을 선택 배율로 bicubic 리사이즈한 뒤 SwinIR·확산 복원을 수행한다. 단순한 네이티브 초해상도 배율과 동일한 의미가 아니다. 결과 저장 전 실제 크기가 원본×배율과 일치하는지 검사한다.

## 구성과 수명 주기

- Core RestorationOptions는 엔진별 옵션을 검증한다. Settings.RestorationProfiles에 모델별 설정을 저장하고 작업별 복사본에만 Restoration을 넣는다. 일반 업스케일 요청에는 이 필드가 없다.
- AdvancedRuntime은 고정 manifest, 환경 메타데이터, 업스트림 revision, 이전 실제 추론 receipt를 확인한다. 누락·손상·비호환 상태는 실행을 차단한다. 준비 상태를 위해 모델을 매번 로드하지 않는다. 실제 작업 시작 시 가중치 SHA256을 다시 검사한다.
- PythonWorker는 기존 세션·제한시간·진행·취소 프로토콜을 유지하며 restore 요청을 추가 전송한다. 고급 모델은 전용 환경/worker 경로로 실행한다. 엔진 변경 전에 기존 worker를 Dispose해 종료를 기다린다.
- 고급 supervisor는 한 번에 한 작업만 처리한다. 이미지별 별도 추론 프로세스가 끝나야 다음 작업을 받는다. 속도보다 오류·VRAM 격리를 우선하며 매 이미지마다 모델을 다시 읽는 비용이 있다.
- 취소는 추론 프로세스를 종료·wait하고 staging/임시 파일을 정리한 뒤 cancelled를 보낸다. 응답이 오지 않으면 기존 C# bridge가 프로세스 트리를 종료한다. 이미 completed한 파일은 보존한다.
- 로딩/Stage 1/Stage 2/저장은 실제 진입점만 표시한다. sampler step 진행률을 추정하지 않으며 이 구간은 indeterminate다. SUPIR의 공식 샘플러 내부 step 막대를 JSON 백분율로 변환하지 않았다.
- CUDA OOM은 실패로 보고하고 이미지별 추론 프로세스 종료로 자원을 반환한다. 검증되지 않은 dtype/CPU/타일 자동 재시도는 하지 않는다. 사용자는 다음 실행에서 타일 설정을 변경할 수 있다.

## 옵션

기본 프리셋은 위 표의 기술 옵션이다. 원본 보존 지향 프리셋은 SUPIR control scale=1.5, DiffBIR steps=20/CFG=4로 매핑한다. 이는 앱의 튜닝 프리셋이며 공식적인 동일성 보증이나 품질 우월성 주장이 아니다. 고급 값을 바꾸면 사용자 설정으로 저장한다. 시드 -1은 작업마다 생성하고 실제 시드는 로컬 작업 로그에 기록한다.

steps 1–100, CFG 1–20, SUPIR control scale 0–2, DiffBIR noise_aug 0–1000, seed -1 또는 0–2147483647, prompt 최대 4000자. 미지원·알 수 없는 옵션과 비유한 수를 거절한다. SUPIR Color Fix는 None/AdaIn/Wavelet이다. DiffBIR는 고정된 Wavelet 결과 색 정합을 사용한다.

타일 메모리 절약은 SUPIR의 공식 tiled VAE(encoder 512 / decoder 64), DiffBIR의 cleaner 512/stride 256, VAE encoder/decoder 256, CLDM 512/stride 256을 켠다. 기존 Real-ESRGAN tile 값을 전달하지 않는다. 저메모리 설정이 모든 입력의 성공을 보장하지 않는다.

## 파일과 오프라인 정책

기존 imaging.read_image를 재사용해 포맷 불일치 거절·EXIF 회전·16비트 회색조의 8비트 변환·TIFF 첫 페이지 정책을 따른다. 모델에는 흰 배경 RGB를 전달한다. PNG 알파는 원본 알파의 bilinear 확대를 합성하고 JPEG는 흰 배경 RGB로 저장한다. ICC/EXIF/16비트 원본 메타데이터는 결과에 보존하지 않는다.

이름은 원본_supir_v0q_x2.png / 원본_supir_v0f_x2.png / 원본_diffbir_v21_x2.png이며 충돌 시 _001 등 순번을 붙인다. 임시 파일을 flush/fsync하고 이미지 검증 후 Windows의 덮어쓰기 없는 rename으로 확정한다. 폴더 실행은 출력 기본 경로 아래 원본폴더명_UpScaled를 사용한다.

설치 단계만 외부 통신을 허용한다. 추론은 HF/Transformers offline 플래그와 Python 소켓 연결 차단을 사용한다. 공식 DiffBIR 자동 다운로드 로더는 manifest의 로컬 파일만 읽도록 대체한다. SUPIR의 CKPT_PTH는 프로세스 내부 모듈로 주입하므로 업스트림 파일을 수정하지 않는다. 네트워크 차단 환경에서 세 모델의 실제 추론을 수행했다. OS 방화벽/패킷 캡처 기반의 별도 감사는 하지 않았다.

artifacts/advanced-jobs에 엔진·프리셋·실제 seed/옵션·배율·가중치 hash/revision을 기록한다. 이미지 바이트나 인증 토큰은 로그에 넣지 않는다. 기술 로그는 stderr, stdout은 JSONL 전용이며 Windows에서도 UTF-8을 강제한다.
