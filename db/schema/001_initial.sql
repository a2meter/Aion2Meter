PRAGMA foreign_keys = ON;

CREATE TABLE metadata (
    singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
    data_version INTEGER NOT NULL CHECK (data_version >= 0),
    schema_version INTEGER NOT NULL CHECK (schema_version > 0)
);

CREATE TABLE protocol_profiles (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    version INTEGER NOT NULL CHECK (version > 0),
    packet_magic BLOB NOT NULL CHECK (length(packet_magic) BETWEEN 1 AND 32),
    party_marker INTEGER NOT NULL CHECK (party_marker BETWEEN 0 AND 255),
    is_active INTEGER NOT NULL DEFAULT 0 CHECK (is_active IN (0, 1))
);

CREATE TABLE protocol_profile_ports (
    profile_id INTEGER NOT NULL REFERENCES protocol_profiles(id) ON DELETE CASCADE,
    port INTEGER NOT NULL CHECK (port BETWEEN 1 AND 65535),
    PRIMARY KEY (profile_id, port)
);

CREATE TABLE message_layouts (
    id INTEGER PRIMARY KEY,
    profile_id INTEGER NOT NULL REFERENCES protocol_profiles(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    max_payload_bytes INTEGER NOT NULL CHECK (max_payload_bytes BETWEEN 1 AND 16777216),
    UNIQUE (profile_id, name),
    UNIQUE (profile_id, id)
);

CREATE TABLE message_fields (
    id INTEGER PRIMARY KEY,
    layout_id INTEGER NOT NULL REFERENCES message_layouts(id) ON DELETE CASCADE,
    field_order INTEGER NOT NULL CHECK (field_order BETWEEN 0 AND 255),
    kind INTEGER NOT NULL CHECK (kind BETWEEN 1 AND 65535),
    flags INTEGER NOT NULL DEFAULT 0 CHECK (flags BETWEEN 0 AND 65535),
    byte_offset INTEGER NOT NULL CHECK (byte_offset >= 0),
    byte_size INTEGER NOT NULL CHECK (byte_size > 0),
    max_count INTEGER NOT NULL DEFAULT 1 CHECK (max_count > 0),
    UNIQUE (layout_id, field_order),
    UNIQUE (layout_id, kind)
);

CREATE TABLE opcodes (
    id INTEGER PRIMARY KEY,
    profile_id INTEGER NOT NULL REFERENCES protocol_profiles(id) ON DELETE CASCADE,
    family INTEGER NOT NULL CHECK (family BETWEEN 1 AND 65535),
    kind INTEGER NOT NULL CHECK (kind BETWEEN 1 AND 65535),
    name TEXT NOT NULL,
    tag BLOB NOT NULL CHECK (length(tag) BETWEEN 1 AND 32),
    layout_id INTEGER,
    FOREIGN KEY (profile_id, layout_id) REFERENCES message_layouts(profile_id, id),
    UNIQUE (profile_id, family, kind),
    UNIQUE (profile_id, tag),
    UNIQUE (profile_id, name)
);

CREATE TABLE bosses (
    id INTEGER PRIMARY KEY,
    code INTEGER NOT NULL UNIQUE CHECK (code > 0),
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE dungeons (
    id INTEGER PRIMARY KEY,
    code INTEGER NOT NULL UNIQUE CHECK (code > 0),
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE dungeon_bosses (
    dungeon_id INTEGER NOT NULL REFERENCES dungeons(id) ON DELETE CASCADE,
    boss_id INTEGER NOT NULL REFERENCES bosses(id) ON DELETE CASCADE,
    encounter_order INTEGER NOT NULL CHECK (encounter_order > 0),
    PRIMARY KEY (dungeon_id, boss_id),
    UNIQUE (dungeon_id, encounter_order)
);

CREATE TABLE mobs (
    id INTEGER PRIMARY KEY,
    code INTEGER NOT NULL UNIQUE CHECK (code > 0),
    name TEXT NOT NULL,
    boss_id INTEGER REFERENCES bosses(id)
);

CREATE TABLE skills (
    id INTEGER PRIMARY KEY,
    code INTEGER NOT NULL UNIQUE CHECK (code > 0),
    name TEXT NOT NULL
);

CREATE TABLE buffs (
    id INTEGER PRIMARY KEY,
    code INTEGER NOT NULL UNIQUE CHECK (code > 0),
    name TEXT NOT NULL
);

CREATE UNIQUE INDEX idx_protocol_profiles_active ON protocol_profiles(is_active) WHERE is_active = 1;
CREATE INDEX idx_protocol_profiles_name ON protocol_profiles(name);
CREATE INDEX idx_profile_ports_profile ON protocol_profile_ports(profile_id, port);
CREATE INDEX idx_message_layouts_profile_name ON message_layouts(profile_id, name);
CREATE INDEX idx_message_fields_layout_order ON message_fields(layout_id, field_order);
CREATE INDEX idx_opcodes_profile_kind ON opcodes(profile_id, family, kind);
CREATE INDEX idx_opcodes_profile_name ON opcodes(profile_id, name);
CREATE INDEX idx_bosses_name ON bosses(name);
CREATE INDEX idx_dungeons_code ON dungeons(code);
CREATE INDEX idx_dungeons_name ON dungeons(name);
CREATE INDEX idx_dungeon_bosses_dungeon ON dungeon_bosses(dungeon_id, encounter_order);
CREATE INDEX idx_dungeon_bosses_boss ON dungeon_bosses(boss_id);
CREATE INDEX idx_mobs_code ON mobs(code);
CREATE INDEX idx_mobs_name ON mobs(name);
CREATE INDEX idx_skills_code ON skills(code);
CREATE INDEX idx_skills_name ON skills(name);
CREATE INDEX idx_buffs_code ON buffs(code);
CREATE INDEX idx_buffs_name ON buffs(name);
