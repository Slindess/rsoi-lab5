# Новый кластер REG.RU для ЛР5

1. Зарегистрироваться/войти в [Рег.облако](https://cloud.reg.ru/), пополнить баланс.
2. Открыть заказ Kubernetes через [инструкцию провайдера](https://reg.cloud/support/cloud/klastery-kubernetes/zakaz-i-upravleniye-uslugoy-klastery-kubernetes/kak-zakazat-klaster-kubernetes).
3. Выбрать стандартный кластер, имя `rsoi-lab5`, одну группу `workers`.
   Для учебного стенда предлагаю начать с одного worker на 2 vCPU / 4 ГБ RAM;
   это стартовая оценка для сервисов, PostgreSQL и будущего Keycloak, не гарантия
   достаточности. При нехватке памяти увеличить тариф. Отказоустойчивый control
   plane для этой стадии не нужен. Проверить итоговую стоимость перед заказом.
4. Выбрать доступную поддерживаемую версию Kubernetes. Версию kubectl в workflow
   затем согласовать с версией API сервера.
5. Заказать кластер, дождаться активации, скачать kubeconfig из карточки кластера.
   Сохранить рядом с репозиторием: `../kubeconfig-regru.yaml`, не коммитить.

## Первая проверка

Из корня репозитория:

```bash
KCFG=../kubeconfig-regru.yaml
chmod 600 "$KCFG"
kubectl --kubeconfig "$KCFG" get nodes -o wide
kubectl --kubeconfig "$KCFG" get pods -A
kubectl --kubeconfig "$KCFG" get storageclass
kubectl --kubeconfig "$KCFG" get ingressclass
kubectl --kubeconfig "$KCFG" get svc -A
```

На этом этапе достаточно Ready у worker и работающих системных Pod.
[Подключение по инструкции REG.RU](https://reg.cloud/support/instrukcii/klasteri-kubernetes/kak-podklyuchitsya-k-klasteru-kubernetes).

## Следующий этап после проверки

Перед установкой Traefik проверить, нет ли уже ingress-контроллера. Перенесённый
workflow ожидает Service `traefik` в namespace `traefik` с внешним IP.
В `deploy/traefik.yaml` удалена аннотация Beget; поддержку LoadBalancer и нужные
настройки REG.RU нужно проверить на созданном кластере. Если внешний IP остаётся
Pending, сначала выяснить требования провайдера, затем менять экспозицию API.

В новом репозитории GitHub понадобятся Secrets `DOCKERHUB_USERNAME`,
`DOCKERHUB_TOKEN`, `KUBE_CONFIG` (полный YAML нового kubeconfig, не base64).
Secrets из репозитория ЛР4 автоматически не переносятся. GitHub runner должен
иметь доступ к Kubernetes API. Пока кластер и Secrets не готовы, deploy не пройдёт.

README и Postman-коллекции ЛР5 сохранены, выбран вариант v2. Autograding запускает
коллекцию ЛР5; Keycloak и JWT настраиваются при деплое, см. [инструкцию](keycloak.md). Успешный workflow с continue-on-error у
шагa autograding не означает успешные API-тесты.

PostgreSQL пока использует hostPath с привязкой к worker, как в ЛР4. Замена worker
может потерять данные. После проверки StorageClass можно отдельно перейти на PVC.
