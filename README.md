--Проверить на каких шардах находятся записи от и для пользователя LadyGaga
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

--,

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

-- Add custom strategy
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

SELECT citus_set_default_rebalance_strategy('isolate_LadyGaga');


--Проверить новую стратегию ребаланса
SELECT * FROM pg_dist_rebalance_strategy;

--Ребаланс
SELECT citus_rebalance_start();

--Еще раз проверить какие шарды на каких воркерах
SELECT shardid, nodename
FROM pg_dist_shard_placement
ORDER BY nodename DESC;