# Advanced 모델 출처와 라이선스

| 구성요소 | 공식 출처 | 확인한 조건 |
|---|---|---|
| SUPIR 코드 / Q·F | https://github.com/Fanghua-Yu/SUPIR | 공식 SUPIR Software License Agreement의 비상업적 이용 제한. 앱의 MIT로 재허가되지 않음 |
| SUPIR 공식 체크포인트 배포 | https://drive.google.com/drive/folders/1yELzm5SvAi9e7kPcO_jPp2XkTs4vK6aR | README 연결된 원본 배포. 바이너리를 Git/앱 패키지에 재배포하지 않음 |
| DiffBIR 코드 | https://github.com/XPixelGroup/DiffBIR | Apache-2.0 |
| DiffBIR Stage 1·SD2.1-zsnr·v2.1 | https://huggingface.co/lxq007/DiffBIR-v2 | 모델 카드의 apache-2.0 표기. SD2.1 기반 모델의 상위 이용 조건도 별도 확인 대상 |
| SDXL base 1.0 0.9 VAE | https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0 | OpenRAIL++ 모델 카드 표기 및 해당 LICENSE 조건 |
| CLIP ViT-L/14 | https://huggingface.co/openai/clip-vit-large-patch14 / https://github.com/openai/CLIP | OpenAI CLIP 원본 MIT 고지 참고. HF API 카드에 license 필드는 없었음 |
| OpenCLIP ViT-bigG | https://huggingface.co/laion/CLIP-ViT-bigG-14-laion2B-39B-b160k | 모델 카드 MIT 표기 |

라이선스 메타데이터 확인일: 2026-11-17. 모델 카드가 하위 파일의 모든 권리를 포괄한다고 단정하지 않는다. 특히 상업적 SUPIR 사용은 허용된 것으로 가정하지 않는다. 실제 코드 원문은 licenses/advanced에 보존한다. 다운로더는 출처·revision·hash만 배포하고 대형 모델 바이너리를 재배포하지 않는다.

LLaVA/RAM은 이번 captioner=none 경로에서 사용하지 않아 해당 가중치를 설치하지 않았다. 나중에 캡셔닝을 활성화하려면 독립된 라이선스/환경/오프라인 검증이 필요하다.
