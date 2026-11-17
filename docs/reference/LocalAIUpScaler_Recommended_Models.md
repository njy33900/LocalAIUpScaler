# LocalAIUpScaler 추천 AI 모델 및 획득처

> 작성 기준: 2026-11-12  
> 대상 프로젝트: https://github.com/njy33900/LocalAIUpScaler  
> 목적: 기존 Real-ESRGAN x4plus 외 모델 도입 후보 선정 및 공식 획득처 기록. **개발 명세가 아니라 모델 선정 참고 문서**이다.

## 1. 요약 및 추천 순서

| 구분 | 모델 | 주요 용도 | 상대적 처리 부담 | 도입 순서 | 통합 방식 |
|---|---|---|---|---|---|
| 기존 | **RealESRGAN_x4plus** | 범용 사진 4× 확대 | 보통 | 구현 유지 | 기존 Real-ESRGAN 엔진 |
| 추가 확정 | **RealESRGAN_x4plus_anime_6B** | 애니메이션/일러스트 4× | 낮음 | **1순위** | Real-ESRGAN 계열 모델 등록 |
| 추가 확정 | **realesr-general-x4v3** | 경량 범용 4×, 노이즈 강도 제어 | 낮음 | **2순위** | 모델 전용 아키텍처와 가중치 등록 |
| 고품질 후보 | **Real_HAT_GAN_SRx4** | 원본에 비교적 충실한 고품질 사진 4× | 높음 | **3순위** | 별도 HAT 추론 어댑터 |
| 고품질 후보 | **Real_HAT_GAN_SRx4_sharper** | 더 선명한 시각적 결과 | 높음 | 3순위 비교 후보 | 별도 HAT 추론 어댑터 |
| 고급 복원 | **SUPIR-v0Q / SUPIR-v0F** | 생성형 고품질 사진 복원 | 매우 높음 | 향후 검토 | 별도 Diffusion 파이프라인 |
| 고급 복원 | **DiffBIR v2.1** | 복합 열화/블라인드 이미지 복원 | 매우 높음 | 향후 검토 | 별도 Diffusion 파이프라인 |
| 후처리 선택 | **GFPGAN v1.4** | 인물 얼굴 복원 | 추가 부담 | 향후 검토 | 얼굴 복원 후처리 |

**추천 결론:** 당장 `anime_6B`, `general-x4v3`를 도입하고 이후 `Real_HAT_GAN_SRx4` 및 `sharper`를 비교한다. SUPIR/DiffBIR은 모델 파일만 바꾸는 확장이 아니므로 별도 개발 과제로 관리한다.

> **주의:** 위의 처리 부담은 절대 성능 측정값이 아니라 구조적 복잡도에 따른 정성적 비교다. 무거운 모델이 반드시 더 높은 원본 충실도나 더 좋은 결과를 제공하지는 않는다.

## 2. 공식 소스 및 모델 다운로드

### 2.1 기존 — RealESRGAN_x4plus

- **목적:** 일반 사진, 풍경, 인물 등 범용 실사 업스케일링.
- **확대:** 네이티브 4×.
- **모델 가중치 (.pth):** [RealESRGAN_x4plus.pth 직접 다운로드](https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth)
- **공식 코드:** https://github.com/xinntao/Real-ESRGAN
- **모델 목록:** https://github.com/xinntao/Real-ESRGAN/blob/master/docs/model_zoo.md
- **통합 메모:** 기존 모델을 유지하고 신규 모델 선택 인터페이스의 기본값으로 제공.

### 2.2 추가 확정 — RealESRGAN_x4plus_anime_6B

- **목적:** 애니메이션, 만화, 디지털 일러스트 등 선화와 평면 색상이 주된 이미지.
- **특징:** 6개 RRDB 블록으로 구성된 비교적 작은 네트워크.
- **확대:** 네이티브 4×.
- **모델 가중치 (.pth):** [RealESRGAN_x4plus_anime_6B.pth 직접 다운로드](https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.2.4/RealESRGAN_x4plus_anime_6B.pth)
- **공식 코드:** https://github.com/xinntao/Real-ESRGAN
- **통합 메모:** `x4plus`와 같은 파일 형식이지만 RRDB 블록 수 등 네트워크 설정은 모델별로 맞춰야 한다. 가중치 파일만 단순 교체하지 말 것.

### 2.3 추가 확정 — realesr-general-x4v3

- **목적:** 빠르고 가벼운 범용 이미지 복원.
- **특징:** 공식 `-dn` 노이즈 제거 강도 옵션 지원. 심한 블러/노이즈 제거는 x4plus보다 약할 수 있음.
- **확대:** 기본 4×; 저배율 출력은 지원 구현의 후처리 방식을 확인할 것.
- **필수 기본 가중치:** [realesr-general-x4v3.pth](https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesr-general-x4v3.pth)
- **노이즈 강도 보간용 가중치:** [realesr-general-wdn-x4v3.pth](https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesr-general-wdn-x4v3.pth)
- **공식 릴리스:** https://github.com/xinntao/Real-ESRGAN/releases
- **통합 메모:** 두 가중치 및 `srvgg` 계열 아키텍처 로딩을 고려한다. UI에 노이즈 강도를 제공할 경우 공식 구현의 보간 로직을 사용한다. x4plus의 RRDBNet으로 로드하면 안 된다.

### 2.4 고품질 후보 — HAT / Real-HAT-GAN

- **목적:** 계산 비용을 감수하고 사진 복원 품질을 비교하고 싶은 경우.
- **추천 우선 검토 모델:** `Real_HAT_GAN_SRx4.pth` (상대적으로 원본 충실도 중심).
- **비교 모델:** `Real_HAT_GAN_SRx4_sharper.pth` (시각적 선명도 중심, 아티팩트 유의).
- **공식 코드 및 다운로드 안내:** https://github.com/XPixelGroup/HAT
- **공식 사전학습 모델 다운로드:** 저장소 README의 `How To Test` → **Google Drive** 또는 **Baidu Netdisk** 링크 이용.
- **모델별 상세 설정:** 해당 저장소 `options/test/` 디렉터리 참고.
- **통합 메모:** HAT 아키텍처와 의존성(BasicSR 계열)이 필요하다. Real-ESRGAN RRDB 가중치처럼 직접 로드할 수 없다. 타일 추론과 모델별 설정 파일을 별도 관리한다.
- **추가 유의:** 일반 `HAT_SRx4`는 합성 저해상도 벤치마크용 초해상도 모델과 실제 이미지용 `Real_HAT_GAN_SRx4`를 구분해야 한다.

### 2.5 고급 생성형 복원 — SUPIR

- **목적:** 저품질 사진의 세부 질감을 생성형 AI로 재구성.
- **선택:** `SUPIR-v0Q`는 일반적인 높은 시각적 품질, `SUPIR-v0F`는 경미한 열화에서 원본 세부 보존을 중시하는 용도.
- **공식 코드:** https://github.com/Fanghua-Yu/SUPIR
- **모델 가중치 획득:** 공식 README → `Dependencies and Installation` → `Download Checkpoints` → `Models we provided`의 **Google Drive** 또는 **Baidu Netdisk** 링크.
- **필요한 추가 모델:** README의 `Dependent Models` 참조. SDXL 기반 체크포인트, CLIP 인코더, LLaVA 등 별도 가중치가 필요할 수 있다.
- **설정 위치:** 공식 `CKPT_PTH.py` 및 `options/SUPIR_v0.yaml`.
- **중요 라이선스:** 공식 저장소에 **비상업적 사용 전용** 제한이 명시되어 있다. 재배포 및 상업적 이용 전 개별 라이선스를 반드시 확인할 것.
- **통합 메모:** 별도 가상환경 및 전용 Worker를 고려한다. 생성형 모델 특성상 원본에 없던 눈·머리카락·질감을 만들어낼 수 있다.

### 2.6 고급 생성형 복원 — DiffBIR v2.1

- **목적:** 블러, 압축 노이즈, 손상 등 다양한 열화 유형을 모르는 상태에서 이미지 복원.
- **추천 시작 체크포인트:** `DiffBIR_v2.1.pt`.
- **공식 코드:** https://github.com/XPixelGroup/DiffBIR
- **공식 모델 목록:** https://github.com/XPixelGroup/DiffBIR#pretrained-models
- **v2.1 체크포인트:** [DiffBIR_v2.1.pt 직접 다운로드 (Hugging Face)](https://huggingface.co/lxq007/DiffBIR-v2/resolve/main/DiffBIR_v2.1.pt)
- **기타 가중치:** 공식 README의 `Pretrained Models`에서 `v2.pth`, `v1_general.pth`, `v1_face.pth`, SwinIR 기반 1단계 모델 등 확인.
- **통합 메모:** 1단계 열화 제거 모델과 2단계 생성 모델 등 파이프라인 구성요소를 별도로 설치해야 할 수 있다. v2.1 체크포인트 하나만으로 즉시 실행 가능하다고 가정하면 안 된다.
- **주의:** 코드가 Apache-2.0이어도 의존 모델과 데이터·가중치의 이용 조건은 별도로 확인한다.

### 2.7 선택적 후처리 — GFPGAN

- **목적:** 흐릿한 인물 얼굴에 한정된 복원 기능. **범용 업스케일러가 아님**.
- **공식 코드:** https://github.com/TencentARC/GFPGAN
- **모델 배포처:** https://github.com/TencentARC/GFPGAN/releases
- **권장 검토 버전:** GFPGAN v1.4 모델.
- **통합 메모:** 전체 사진 업스케일링 모델과 별개인 `얼굴 복원` 스위치로 제공. 원본 인물의 특징을 변형할 수 있으므로 기본은 비활성화.

## 3. LocalAIUpScaler 통합 시 기술 지침

### 3.1 모델 파일 ≠ 실행 엔진

1. **Real-ESRGAN 계열**: 기존 Python Worker에서 모델별 네트워크 구성과 가중치 로더를 분기한다.
2. **HAT 계열**: HAT 라이브러리/설정 파일에 기반한 별도 어댑터를 추가한다.
3. **SUPIR / DiffBIR**: 의존성 충돌, 용량, 프로세스 수명, 메모리 사용 및 진행률 산정 방식이 다르므로 별도 Worker/가상환경을 검토한다.
4. **GFPGAN**: 기본 초해상도 모델을 교체하는 것이 아니라 옵션형 후처리로 분리한다.

### 3.2 UI 권장 형태

- 모델 선택 콤보박스: `범용 (x4plus)`, `경량 (general-x4v3)`, `애니메이션 (anime_6B)`.
- 아직 통합되지 않은 모델은 목록에서 숨기거나 `설치되지 않음`으로 표시하며, 선택 시 실행 가능 상태를 검증한다.
- 기존 **단일 이미지 / 현재 폴더 전체 처리**, **진행 Modal**, **메인 창 조작 차단** 방식은 변경하지 않는다.
- 모델 이름, 대상 이미지 유형, 모델 파일 설치 상태, 선택 가능한 옵션을 UI에 표시한다.
- 특정 GPU 모델/VRAM 용량으로 실행을 제한하지 않는다. 지원 백엔드와 메모리 여유를 런타임에 확인한다.

### 3.3 제안하는 모델 레지스트리

```text
models/
  realesrgan/
    RealESRGAN_x4plus.pth
    RealESRGAN_x4plus_anime_6B.pth
    realesr-general-x4v3.pth
    realesr-general-wdn-x4v3.pth
  hat/                     # 추후
    Real_HAT_GAN_SRx4.pth
    Real_HAT_GAN_SRx4_sharper.pth
  supir/                   # 추후, 실제 종속 파일 구성에 따라 분리
  diffbir/                 # 추후, 실제 종속 파일 구성에 따라 분리
  gfpgan/                  # 선택적 후처리
```

### 3.4 Codex 구현 단계 추천

**Phase A — 바로 추가**

- [ ] 모델 선택 UI 및 모델 메타데이터 레지스트리
- [ ] `anime_6B` 아키텍처/가중치 로더 추가
- [ ] `general-x4v3` 아키텍처/가중치 로더 추가
- [ ] `general-wdn-x4v3`와 노이즈 강도 제어 연결
- [ ] 단일 이미지와 폴더 단위 모두에서 모델 선택 적용
- [ ] 각 모델 로딩 실패, 파일 누락, 처리 취소 및 복귀 검증

**Phase B — 품질 비교 후 검토**

- [ ] HAT 모델 2종의 독립 추론 구현 및 비교
- [ ] 공통 이미지 세트로 원본 충실도/인공 질감/처리 시간 평가

**Phase C — 별도 개발 과제로 검토**

- [ ] SUPIR 라이선스, 외부 의존 체크포인트, 설치 용량과 오프라인 배포 검토
- [ ] DiffBIR 전체 추론 파이프라인 및 모델 다운로드 확인
- [ ] GFPGAN 선택 후처리와 원본 얼굴 비교

## 4. 다운로드 및 배포 주의사항

- 공식 저장소나 저자가 명시한 체크포인트 배포처를 우선 사용한다.
- 개발·설치 시에는 다운로드를 허용할 수 있지만, **실제 이미지 처리 자체는 로컬 오프라인**으로 설계한다.
- 외부 다운로드 파일은 배포 시 해시(SHA-256)를 기록하고 신뢰할 수 없는 체크포인트의 Python pickle 역직렬화 위험을 고려한다.
- 체크포인트와 라이브러리를 소스 저장소에 무조건 커밋하지 말고, 별도 모델 설치/배포 정책을 만든다.
- 오픈소스 코드의 라이선스와 개별 모델 가중치, 의존 모델의 조건은 다를 수 있으므로 각각 검토한다.
- SUPIR은 특히 비상업적 사용 제한을 확인해야 한다.

## 5. 출처 (공식)

1. Real-ESRGAN 코드 및 모델 목록: https://github.com/xinntao/Real-ESRGAN
2. Real-ESRGAN 릴리스: https://github.com/xinntao/Real-ESRGAN/releases
3. HAT 코드, 사전학습 가중치 안내: https://github.com/XPixelGroup/HAT
4. SUPIR 코드, 체크포인트 및 이용 제한: https://github.com/Fanghua-Yu/SUPIR
5. DiffBIR 코드 및 체크포인트: https://github.com/XPixelGroup/DiffBIR
6. GFPGAN 코드/릴리스: https://github.com/TencentARC/GFPGAN

---

**정리:** `anime_6B` 및 `general-x4v3`는 현 프로젝트에 우선 추가, `Real-HAT-GAN`은 다음 고품질 후보, `SUPIR`과 `DiffBIR`은 독립적인 고급 복원 기능으로 추후 검토한다.
