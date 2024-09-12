## О проекте
Домашнее задание по шардированию.  
Проект состоит из следующих компонентов:  
* Приложение .NET WebApi в папке ./server, которое собирается в образ server:local и контейнер server.  
* Dockerfile и сид базы данных координатора Postgres/Citus в папке ./db, которые собираются в образ db:local (контейнер pg_master). Библиотекой Faker сгенерированы пользователи, френды, посты.
* Dockerfile для воркеров Postgres/Citus в папке ./citus-worker, которые собираются в образ citus-worker:local (контейнеры pg_worker1,pg_worker2,pg_worker3).
* В папке tests находятся запросы для расширения VSCode REST Client и экспорты коллекций и окружений Postman.
## Начало работы
Склонировать проект, сделать cd в корень репозитория и запустить Docker Compose.  
Дождаться статуса healthy на контейнере pg_master - контейнер станет healthy когда будет загружен сид.  
```bash
https://github.com/npctheory/highload-queries.git
cd highload-queries
docker compose up --build -d
```
## Контроллер диалогов
Получение списка диалогов, получение сообщений диалога, отправка сообщений пользователю.  



## Шардинг  
Хранилище диалогов реализуется классом CitusDialogMessageRepository.  
Для задач решардинга хранилище сообщений денормализовано - при отправке сообщения пишутся в две таблицы: dialog_messages_sent и dialog_messages_received. При чтении полученные сообщения запрашиваются из dialog_messages_sent а отправленные из dialog_messages_received.  
Таблица dialog_messages_sent сегментирована в Citus по ключу sender_id, Таблица dialog_messages_received сегментирована по ключу receiver_id.  
При первоначальном запуске приложения сид пользовательских сообщений сегментируется и распределяется по двум узлам pg_worker1 и pg_worker2.  
Начальное состояние кластера Citus (файл ./db/initdb/init201.sql):  
```sql  
\c highloadsocial;
CREATE EXTENSION citus;
SELECT master_add_node('pg_worker1', 5432);
SELECT master_add_node('pg_worker2', 5432);

SELECT create_reference_table('users');
SELECT create_distributed_table('dialog_messages_sent', 'sender_id');
SELECT create_distributed_table('dialog_messages_received', 'receiver_id');
```
### Решардинг  
Пример решардинга. На видео к кластеру добавлеяется узел pg_worker3, создается кастомная стратегия ребаланса, в которой шардам с сообщениями от и для vip-пользователя LadyGaga выделяется отдельный воркер.

<details><summary>Код SQL-запросов</summary>--Проверить на каких шардах находятся записи от и для пользователя LadyGaga
SELECT s.shardid, s.logicalrelid, u.id
FROM pg_dist_shard s
JOIN users u ON
    u.id IN ('LadyGaga')
WHERE
    s.logicalrelid IN ('dialog_messages_sent'::regclass, 'dialog_messages_received'::regclass)
    AND CAST(s.shardminvalue AS bigint) <= hashtext(u.id)
    AND CAST(s.shardmaxvalue AS bigint) >= hashtext(u.id);
	
--Изолировать записи от и для полльзователя LadyGaga на отдельных шардах
SELECT isolate_tenant_to_new_shard('dialog_messages_sent', 'LadyGaga', 'CASCADE');
SELECT isolate_tenant_to_new_shard('dialog_messages_received', 'LadyGaga', 'CASCADE');

--

--Добавить третьего воркера
SELECT master_add_node('pg_worker3', 5432);


--Проверить какие шарды на каких воркерах
SELECT shardid, nodename
FROM pg_dist_shard_placement
WHERE shardid in (,)

--Создать стратегию ребаланска: Шарды LadyGaga получают свой воркер - pg_worker3. Остальные шарды остаются на 1 и 2.
CREATE FUNCTION isolate_LadyGaga_on_pg_worker3(shardid bigint, nodeidarg int)
RETURNS boolean AS $$
SELECT
    (CASE
        WHEN nodename = 'pg_worker3' THEN shardid IN (,)
        ELSE shardid NOT IN (,)
    END)
FROM pg_dist_node WHERE nodeid = nodeidarg
$$ LANGUAGE sql;

CREATE FUNCTION no_capacity_for_pg_worker3(nodeidarg int)
    RETURNS real AS $$
    SELECT
        (CASE WHEN nodename = 'pg_worker3' THEN 0 ELSE 1 END)::real
    FROM pg_dist_node where nodeid = nodeidarg
    $$ LANGUAGE sql;
	
CREATE FUNCTION no_cost_for_LadyGaga(shardid bigint)
RETURNS real AS $$
SELECT
    (CASE WHEN shardid IN (,) THEN 0 ELSE 1 END)::real
$$ LANGUAGE sql;

-- Добавить созданные функции в таблицу стратегий
INSERT INTO pg_dist_rebalance_strategy (
    name,
    default_strategy,
    shard_cost_function,
    node_capacity_function,
    shard_allowed_on_node_function,
    default_threshold,
    minimum_threshold,
    improvement_threshold
) VALUES (
    'isolate_LadyGaga',false,'no_cost_for_LadyGaga','no_capacity_for_pg_worker3','isolate_LadyGaga_on_pg_worker3',0,0,0);

--Сделать новую стратегию стратегией по умолчанию
SELECT citus_set_default_rebalance_strategy('isolate_LadyGaga');


--Проверить что новая стратегия добавлена в таблицу и используется
SELECT * FROM pg_dist_rebalance_strategy;

--Ребаланс
SELECT citus_rebalance_start();

--Еще раз проверить какие шарды на каких воркерах
SELECT shardid, nodename
FROM pg_dist_shard_placement
ORDER BY nodename DESC;</details>