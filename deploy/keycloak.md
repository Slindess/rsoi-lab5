# Keycloak и JWT для ЛР5

Внешний адрес: `http://89.108.100.165/auth`. Домен и DNS не нужны.
Ingress `/auth` ведёт в Keycloak, `/` — в Gateway. Все ресурсы приложения
и Keycloak находятся в namespace `test`, Traefik — в `traefik`.

## Деплой

После push в master workflow собирает образы с тегом SHA и запускает
`scripts/deploy.sh`. Скрипт выполняет по порядку:

1. Развёртывает PostgreSQL приложения.
2. Создаёт Secret `keycloak-credentials` со случайными паролями БД, администратора,
   тестового пользователя и секретом клиента, если Secret ещё не существует.
3. Развёртывает отдельный PostgreSQL Keycloak и сам Keycloak, ждёт готовности.
4. Развёртывает сервисы с внешним issuer и внутренним адресом JWKS.

Secret не меняется при повторном запуске. Не удалять его отдельно от данных БД.
Обе БД используют hostPath и закреплены за worker: удаление worker может потерять
данные. Helm сохраняет выбранный worker при следующих деплоях.

Импорт создаёт realm `rsoi`, confidential-клиента `booking` с Direct Access Grants,
пользователя `test-user` и audience `booking-api`. Секреты подставляются из окружения
контейнера, в файле realm их значений нет. При старте существующий realm пропускается:
изменения JSON не обновляют существующий realm автоматически.

## Проверки после GitHub Actions

```bash
kubectl --kubeconfig ../kubeconfig-regru.yaml -n test get pods,svc,ingress
curl --fail http://89.108.100.165/manage/health
curl --fail http://89.108.100.165/auth/realms/rsoi/.well-known/openid-configuration
curl -i http://89.108.100.165/api/v1/hotels
```

Ожидается: health — 200, metadata — 200, запрос API без токена — 401.
При проблемах:

```bash
kubectl --kubeconfig ../kubeconfig-regru.yaml -n test logs deployment/keycloak --tail=100
kubectl --kubeconfig ../kubeconfig-regru.yaml -n test logs deployment/keycloak-db --tail=100
kubectl --kubeconfig ../kubeconfig-regru.yaml -n test logs deployment/gateway-service --tail=100
```

## Проверка токенов

Все четыре сервиса проверяют RS256-подпись, issuer, audience `booking-api`,
`exp`/`nbf` и наличие `preferred_username`. Защищены `/api/**` и `/internal/**`,
исключения — `/api/v1/authorize` и `/api/v1/callback`; `/manage/health` открыт.
Gateway получает имя из JWT и передаёт исходный Bearer-токен внутренним сервисам.
Loyalty и Reservation получают пользователя из проверенного JWT.

JWKS загружается один раз при старте по внутреннему адресу Keycloak. Валидация
каждого запроса выполняется локально, без introspection и запросов к провайдеру.
После ротации ключей перезапустить все четыре сервиса; при недоступном JWKS
новый экземпляр сервиса не стартует, уже работающий продолжает проверять токены.

Фоновая повторная отмена Loyalty сохраняет исходный токен. Если он истёк до
восстановления Loyalty, повтор прекращается с записью в лог: обхода авторизации
или замены пользователя служебным токеном нет.

## Autograding

Коллекция `v2` адаптирована только в запросе токена: Keycloak использует
`/protocol/openid-connect/token`, `client_id`, `client_secret`. Проверки API сохранены.

Workflow получает тестовый пароль и client secret из Kubernetes Secret и создаёт
временный Postman environment с правами 0600. Значения маскируются в логах,
файл удаляется в конце job. В команду autograding явно передаются файл окружения,
`BASE_URL`, `CONFIG_PATH` и `serviceUrl`, поскольку action очищает окружение.
Google Sheets action отсутствует. У autograding остаётся `continue-on-error: true`:
результат тестов нужно смотреть в его логе.

## Локальный запуск

Создать `.env` со случайными значениями (файл исключён из Git):

```bash
python3 - <<'PY'
from pathlib import Path
import os, secrets
keys = ['KEYCLOAK_DB_PASSWORD', 'KEYCLOAK_ADMIN_PASSWORD',
        'BOOKING_CLIENT_SECRET', 'BOOKING_TEST_PASSWORD']
with os.fdopen(os.open('.env', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), 'w') as output:
    output.write(''.join(key + '=' + secrets.token_urlsafe(32) + '\n' for key in keys))
PY
docker compose up -d --build
```

Gateway: `http://localhost:8080`, Keycloak: `http://localhost:8081/auth`.
Сервисы могут перезапуститься, пока Keycloak впервые импортирует realm.
Compose использует отдельные volumes для БД приложения и Keycloak.

Основания конфигурации: [импорт realm](https://www.keycloak.org/server/importExport),
[пути reverse proxy](https://www.keycloak.org/server/reverseproxy),
[management health endpoints](https://www.keycloak.org/server/management-interface).
