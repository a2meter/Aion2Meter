INSERT INTO protocol_profiles(id, name, version, packet_magic, party_marker, is_active)
VALUES (1, 'aion2-2026-07-10', 1, X'060036', 151, 1);

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
    (11, 1, 'character_lookup', 1048576);

INSERT INTO opcodes(id, profile_id, family, kind, name, tag, layout_id) VALUES
    (1, 1, 1, 1, 'damage', X'0438', 1),
    (2, 1, 1, 2, 'dot_or_heal', X'0538', 2),
    (3, 1, 1, 3, 'battle_stats', X'2A38', 3),
    (4, 1, 1, 4, 'battle_stats_alt', X'2B38', 4),
    (5, 1, 1, 5, 'self_info', X'3336', 5),
    (6, 1, 1, 6, 'other_info', X'4536', 6),
    (7, 1, 1, 7, 'mob_spawn', X'4136', 7),
    (8, 1, 1, 8, 'boss_hp', X'018D', 8),
    (9, 1, 1, 9, 'guard', X'0336', 9),
    (10, 1, 1, 10, 'entity_removed', X'218D', 10),
    (11, 1, 1, 11, 'character_lookup', X'4F36', 11),
    (101, 1, 2, 101, 'party_list', X'0197', NULL),
    (102, 1, 2, 102, 'party_update', X'0297', NULL),
    (103, 1, 2, 103, 'party_dungeon_exit', X'0497', NULL),
    (104, 1, 2, 104, 'party_request', X'0797', NULL),
    (105, 1, 2, 105, 'party_accept', X'0B97', NULL),
    (106, 1, 2, 106, 'party_board_control', X'1397', NULL),
    (107, 1, 2, 107, 'party_leave', X'1D97', NULL),
    (108, 1, 2, 108, 'party_board_refresh', X'2A97', NULL);

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
