# Команды для деплоя

Инструкция по созданию нового кластера: [REG.RU](regru-cluster.md).

Перенесена база ЛР4; OAuth2 и Keycloak пока не реализованы.

Все команды из корня репозитория. Целевой кластер — REG.RU, namespace — `test`, вариант — `v2`.

Путь к kubeconfig задаю явно, контекст по умолчанию не меняю:

```bash
KCFG=../kubeconfig-regru.yaml
```

## Состояние кластера

```bash
kubectl --kubeconfig "$KCFG" get nodes
kubectl --kubeconfig "$KCFG" get pods -A
kubectl --kubeconfig "$KCFG" -n test get pods,svc,ingress
kubectl --kubeconfig "$KCFG" get ingressclass
helm --kubeconfig "$KCFG" list -A
```

## Traefik

Установка в новый кластер или обновление настроек:

```bash
helm repo add traefik https://traefik.github.io/charts
helm repo update
helm upgrade --install traefik traefik/traefik \
  --kubeconfig "$KCFG" \
  --namespace traefik --create-namespace \
  -f deploy/traefik.yaml --wait --timeout 5m
```

Внешний IP и проверка Gateway:

```bash
kubectl --kubeconfig "$KCFG" -n traefik get svc traefik
API_IP=$(kubectl --kubeconfig "$KCFG" -n traefik get svc traefik \
  -o jsonpath='{.status.loadBalancer.ingress[0].ip}')
curl --fail "http://$API_IP/manage/health"
```

Маршрут запроса: балансировщик провайдера → Traefik → Ingress → Gateway.
Остальные сервисы и PostgreSQL доступны внутри кластера через ClusterIP.

## Деплой руками

Нужны Helm, kubectl, Python 3 и уже опубликованные образы с выбранным тегом:

```bash
IMAGE_TAG=$(git rev-parse HEAD)
./scripts/deploy.sh "$KCFG" "$IMAGE_TAG" test
```

Один chart `helm/booking-service` на четыре сервиса, параметры — в
`deploy/*-service.yaml`. Отдельный chart `helm/postgres` для базы.

Посмотреть YAML без установки:

```bash
helm template loyalty-service helm/booking-service -f deploy/loyalty-service.yaml
helm lint helm/booking-service -f deploy/gateway-service.yaml
```

## Логи и диагностика

```bash
kubectl --kubeconfig "$KCFG" -n test logs deployment/gateway-service --tail=100
kubectl --kubeconfig "$KCFG" -n test logs deployment/loyalty-service --tail=100
kubectl --kubeconfig "$KCFG" -n test logs deployment/postgres --tail=100
kubectl --kubeconfig "$KCFG" -n test describe deployment loyalty-service
kubectl --kubeconfig "$KCFG" -n test get events --sort-by=.metadata.creationTimestamp
```

## Сценарий отказоустойчивости из ЛР4

Этот скрипт сохранён из ЛР4. Коллекции ЛР5 имеют другую структуру, поэтому
его не используем для autograding ЛР5. Команда ниже применима только с
коллекцией ЛР4, содержащей папки success и step1–step4.

## Архивная команда тестов ЛР4

```bash
npm install -g newman@6
BASE_URL="http://$API_IP" ./scripts/test-script.sh \
  v2 loyalty-service test "$KCFG"
```

Скрипт отключает и включает Loyalty Service, после завершения возвращает
исходное число реплик, в том числе при ошибке.

## GitHub Actions

Push в `master`: сборка → unit-тесты → проверка charts → публикация образов →
деплой → autograding ЛР5. До реализации OAuth2 ожидаются ошибки API-тестов. Для PR только сборка и проверки. Есть ручной запуск workflow.

Образы в `mslinyakov/*-service`, теги — SHA коммита и `latest`.
Для деплоя используется SHA. Адрес API берётся из Service traefik автоматически.

Секреты репозитория (Settings → Secrets and variables → Actions):

- `DOCKERHUB_USERNAME` — `mslinyakov`.
- `DOCKERHUB_TOKEN` — токен с правом записи.
- `KUBE_CONFIG` — содержимое YAML нового кластера REG.RU, без base64.

Образы публичные. Kubeconfig и токены в Git не кладу.

Особенность `education/autograding@v1`: очищает переменные окружения тестов.
Поэтому workflow подставляет `BASE_URL`, `CONFIG_PATH` и параметр Newman
`serviceUrl` прямо в команду
`autograding.json` на runner. У autograding стоит `continue-on-error: true`:
зелёный workflow ещё не означает 10/10, результат смотрю в логе этого шага.

## База

Один PostgreSQL, базы `reservations`, `payments`, `loyalties`.
Таблицы и тестовые данные создаются сервисами при запуске.
Secret `booking-database` с паролем и строками подключения создаётся один раз.
Его нельзя удалять отдельно от данных: новый Secret не поменяет пароль в базе.

Перенесённый chart использует hostPath, а не PVC/StorageClass. Данные в `/var/lib/rsoi-lab5/postgres`,
PostgreSQL закреплён за одним worker. Перезапуск Pod данные сохраняет,
пересоздание worker — нет. При недоступности этого worker база автоматически
на другой узел не переезжает.
