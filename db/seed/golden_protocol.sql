INSERT INTO protocol_profiles(id, name, version, packet_magic, party_marker, is_active)
-- Bootstrap/test profile. Field encodings below satisfy the authoritative decoder
-- contract but are not a claim of full-PCAP semantic certification. Task 8 certified
-- only entity removal; Task 12 owns production golden descriptor closure.
VALUES (1, 'bootstrap-test-aion2-2026-07-10', 1, X'060036', 151, 1);

INSERT INTO metadata(singleton_id, data_version, schema_version) VALUES (1, 1, 1);
INSERT INTO protocol_profile_ports(profile_id, port) VALUES (1, 13328);

INSERT INTO message_layouts(id, profile_id, name, max_payload_bytes) VALUES
    (1, 1, 'damage', 1048576),
    (2, 1, 'dot_or_heal', 1048576),
    (3, 1, 'battle_stats', 1048576),
    (4, 1, 'battle_stats_alt', 1048576),
    (5, 1, 'self_info', 1048576),
    (6, 1, 'other_info', 1048576),
    (7, 1, 'mob_spawn', 1048576),
    (8, 1, 'boss_hp', 1048576),
    (9, 1, 'guard', 1048576),
    (10, 1, 'entity_removed', 1048576),
    (11, 1, 'character_lookup', 1048576),
    (12, 1, 'party_common_bootstrap', 1048576),
    (13, 1, 'content_exit_bootstrap', 1048576);

INSERT INTO opcodes(id, profile_id, family, kind, name, tag, layout_id) VALUES
    (1, 1, 1, 1, 'damage', X'0438', 1),
    (2, 1, 1, 2, 'dot_or_heal', X'0538', 2),
    (3, 1, 1, 3, 'battle_stats', X'2A38', 3),
    (4, 1, 1, 4, 'battle_stats_alt', X'2B38', 4),
    (5, 1, 1, 5, 'self_info', X'3336', 5),
    (6, 1, 1, 6, 'other_info', X'4536', 6),
    (7, 1, 1, 7, 'mob_spawn', X'4136', 7),
    (8, 1, 1, 8, 'boss_hp', X'018D', 8),
    (9, 1, 1, 9, 'guard', X'0336', NULL),
    (10, 1, 1, 10, 'entity_removed', X'218D', 10),
    (11, 1, 1, 11, 'character_lookup', X'4F36', 11),
    (101, 1, 2, 101, 'party_list', X'0197', 12),
    (102, 1, 2, 102, 'party_update', X'0297', 12),
    (103, 1, 2, 103, 'party_dungeon_exit', X'0497', 13),
    (104, 1, 2, 104, 'party_request', X'0797', 12),
    (105, 1, 2, 105, 'party_accept', X'0B97', 12),
    (106, 1, 2, 106, 'party_board_control', X'1397', 12),
    (107, 1, 2, 107, 'party_leave', X'1D97', 12),
    (108, 1, 2, 108, 'party_board_refresh', X'2A97', 12);

-- kind, flags, offset, size, max-count use the fixed canonical bootstrap layout.
INSERT INTO message_fields(id, layout_id, field_order, kind, flags, byte_offset, byte_size, max_count) VALUES
    (1001,1,0,1,0,0,4,1),(1002,1,1,2,0,4,4,1),(1003,1,2,4,0,12,4,1),(1004,1,3,13,0,44,8,1),(1005,1,4,14,0,52,8,1),(1006,1,5,15,0,60,8,1),(1007,1,6,18,0,84,4,1),(1008,1,7,22,0,94,1,1),(1009,1,8,23,0,95,1,1),
    (2001,2,0,1,0,0,4,1),(2002,2,1,2,0,4,4,1),(2003,2,2,4,0,12,4,1),(2004,2,3,13,0,44,8,1),(2005,2,4,14,0,52,8,1),(2006,2,5,15,0,60,8,1),(2007,2,6,18,0,84,4,1),(2008,2,7,22,0,94,1,1),(2009,2,8,23,0,95,1,1),
    (3001,3,0,2,0,4,4,1),(3002,3,1,3,0,8,4,1),(3003,3,2,5,0,16,4,1),(3004,3,3,19,0,88,4,1),(3005,3,4,21,0,93,1,1),
    (4001,4,0,2,0,4,4,1),(4002,4,1,3,0,8,4,1),(4003,4,2,5,0,16,4,1),(4004,4,3,19,0,88,4,1),(4005,4,4,21,0,93,1,1),
    (5001,5,0,1,0,0,4,1),(5002,5,1,3,0,8,4,1),(5003,5,2,11,0,40,2,1),(5004,5,3,12,0,42,2,1),(5005,5,4,24,0,96,1,1),(5006,5,5,26,2,98,1,20),
    (6001,6,0,1,0,0,4,1),(6002,6,1,3,0,8,4,1),(6003,6,2,11,0,40,2,1),(6004,6,3,12,0,42,2,1),(6005,6,4,24,0,96,1,1),(6006,6,5,26,2,98,1,20),
    (7001,7,0,1,0,0,4,1),(7002,7,1,3,0,8,4,1),(7003,7,2,6,0,20,4,1),(7004,7,3,7,0,24,4,1),(7005,7,4,16,0,68,8,1),(7006,7,5,17,0,76,8,1),(7007,7,6,25,0,97,1,1),(7008,7,7,26,2,98,1,20),
    (8001,8,0,1,0,0,4,1),(8002,8,1,7,0,24,4,1),(8003,8,2,16,0,68,8,1),(8004,8,3,17,0,76,8,1),
    (10001,10,0,1,0,0,4,1),
    (11001,11,0,1,0,0,4,1),(11002,11,1,3,0,8,4,1),(11003,11,2,11,0,40,2,1),(11004,11,3,12,0,42,2,1),(11005,11,4,24,0,96,1,1),(11006,11,5,26,2,98,1,20),
    (12001,12,0,1,0,0,4,1),(12002,12,1,8,0,28,4,1),(12003,12,2,9,0,32,4,1),(12004,12,3,10,0,36,4,1),(12005,12,4,21,0,93,1,1),(12006,12,5,26,2,98,1,20),
    (13001,13,0,8,0,28,4,1),(13002,13,1,9,0,32,4,1),(13003,13,2,20,0,92,1,1),(13004,13,3,26,2,98,1,20);

INSERT INTO dungeons(id, code, name) VALUES (1, 600153, 'Golden protocol content 600153');
INSERT INTO bosses(id, code, name) VALUES
    (1, 2301721, 'Turgen'),
    (2, 2301722, 'Griosa'),
    (3, 2301723, 'Basilus');
INSERT INTO dungeon_bosses(dungeon_id, boss_id, encounter_order) VALUES
    (1, 1, 1),
    (1, 2, 2),
    (1, 3, 3);
INSERT INTO mobs(id, code, name, boss_id) VALUES
    (1, 2301721, 'Turgen', 1),
    (2, 2301722, 'Griosa', 2),
    (3, 2301723, 'Basilus', 3);
