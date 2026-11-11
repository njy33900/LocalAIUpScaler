# Worker 프로토콜 v1

stdin/stdout: UTF-8, 한 줄에 JSON 객체 하나. stdout은 프로토콜 전용이고 기술 로그는 stderr입니다. 모든 객체에 `protocol_version`, `type`, `job_id`가 있습니다. 시작 시 job_id가 빈 문자열인 ready 이벤트를 한 번 보냅니다.

명령:
- `hello`, `get_devices`: `device_info` 응답. CUDA 감지 결과, 장치명, 엔진 ID, 지원 배율·설정 스키마.
- `load_model`: device/precision 지정, 로컬 모델 준비 후 completed. 같은 장치/정밀도는 모델 재사용.
- `upscale`: `input`, `output_dir`, `device`, `precision`, `format`, `tile_size`. scale=4, engine=realesrgan_x4plus만 허용.
- `cancel`: 해당 job_id에만 적용. 다른 작업의 취소는 무시.
- `shutdown`: 활성 작업 취소 후 종료.

예시:
```json
{"protocol_version":1,"type":"upscale","job_id":"a1b2","input":"C:\\Pictures\\sample.png","output_dir":"C:\\Pictures\\Upscaled","device":"auto","precision":"auto","format":"PNG","tile_size":0}
```

`job_id`는 영문·숫자·밑줄·하이픈 1~80자입니다. UI 브리지는 매번 UUID를 생성합니다. 동시 추론은 하나이며 추가 작업은 실패 응답으로 거절합니다. 폴더 순차 실행은 C# BatchRunner가 담당합니다.

이벤트:
- `ready`, `device_info`
- `progress`: stage=decoding/loading/inference/retry/saving. 추론 progress=0~100, 그 외 null이면 무한 진행 표시. 타일 재시도 시 이전 진행률을 이어 계산하지 않음.
- `completed`: output/output_width/output_height/device/precision. 저장 및 이름 확정 이후 발행.
- `failed`: message/code. 상세 traceback은 stderr.
- `cancelled`: 임시 파일 정리 후 발행.

취소는 타일 사이와 저장 전후에 확인합니다. 브리지는 취소 응답을 최대 15초 기다린 뒤 프로세스 트리를 종료하고 종료를 확인한 다음 해당 UUID의 임시 파일만 제거합니다. 시작 제한시간은 90초, 요청 제한시간은 30분이며 WorkerOptions로 조정할 수 있습니다. 시간 제한은 성능 예측이 아니라 무응답 작업을 종료하기 위한 운영 정책입니다.

각 프로세스의 읽기 작업·ready·진행 중 요청은 세션 내부에 소속되어 이전 프로세스 종료가 새 요청을 오염시키지 않습니다. 실패/강제 종료 뒤 다음 작업은 Worker를 다시 시작합니다. 취소 요청 직전에 최종 파일 확정이 끝났다면 completed로 집계합니다.

명세 예시의 `output` 대신 `output_dir`를 사용합니다. 최종 파일명 충돌 결정은 실제 저장 주체인 Worker만 수행하여 확인 후 생성 사이의 경합을 피합니다.
