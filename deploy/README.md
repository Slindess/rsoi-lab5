# Деплой ЛР5

- [Создание кластера REG.RU](regru-cluster.md)
- [Keycloak, JWT, проверки и локальный запуск](keycloak.md)

Вариант v2, namespace приложения `test`, Traefik в namespace `traefik`.
Публичный адрес API — `http://89.108.100.165`, Keycloak — `/auth` на том же IP.

Push в master: сборка и unit-тесты → проверка charts → публикация четырёх образов
в Docker Hub с тегом SHA → деплой PostgreSQL, Keycloak и сервисов → autograding.
Для PR выполняются сборка, тесты и проверка charts. Доступен ручной запуск workflow.

GitHub Actions Secrets: `DOCKERHUB_USERNAME`, `DOCKERHUB_TOKEN`, `KUBE_CONFIG`
(полный YAML kubeconfig нового кластера, не base64).

Ручной деплой опубликованного тега:

```bash
./scripts/deploy.sh ../kubeconfig-regru.yaml <image-tag> test
```

Секреты баз и Keycloak создаются один раз и хранятся только в Kubernetes.
PostgreSQL использует hostPath с привязкой к worker. Удалять Secret отдельно
от данных или пересоздавать worker без резервной копии нельзя.

`scripts/test-script.sh` сохранён как сценарий ЛР4 и требует коллекции ЛР4 с папками
success и step1–step4. Autograding ЛР5 запускает коллекцию v2 напрямую через Newman.
