using System.Text.Json;

namespace Namter.Encounter;

public static class EncounterRecordWriter
{
    public static byte[] Write(EncounterRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("id", record.Id);
            writer.WriteString("startUtc", DateTimeOffset.FromUnixTimeMilliseconds(record.StartTimestampMs));
            writer.WriteString("endUtc", DateTimeOffset.FromUnixTimeMilliseconds(record.EndTimestampMs));
            writer.WriteBoolean("isComplete", record.IsComplete);
            writer.WriteString("completionReason", record.CompletionReason.ToString());
            WriteIdentity(writer, record.Encounter);
            writer.WriteStartArray("participants");
            foreach (ParticipantRecord p in record.Participants.OrderBy(x => x.ActorId))
            {
                writer.WriteStartObject(); writer.WriteNumber("actorId", p.ActorId); writer.WriteString("name", p.Name);
                writer.WriteNumber("jobId", p.JobId); writer.WriteBoolean("isSelf", p.IsSelf);
                writer.WriteNumber("damage", p.Damage); writer.WriteNumber("multiDamage", p.MultiDamage);
                writer.WriteNumber("dotDamage", p.DotDamage); writer.WriteNumber("healing", p.Healing); writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("entities");
            foreach (EntityRecord e in record.Entities.OrderBy(x => x.ActorId))
            {
                writer.WriteStartObject(); writer.WriteNumber("actorId", e.ActorId); writer.WriteNumber("ownerActorId", e.OwnerActorId);
                writer.WriteNumber("mobCode", e.MobCode); writer.WriteString("kind", e.Kind.ToString()); writer.WriteString("name", e.Name); writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("events");
            foreach (DamageRecord e in record.Events) WriteEvent(writer, e);
            writer.WriteEndArray();
            writer.WriteStartArray("buffWindows");
            foreach (BuffWindowRecord b in record.BuffWindows.OrderBy(x => x.StartTimestampMs).ThenBy(x => x.OwnerId).ThenBy(x => x.TargetId).ThenBy(x => x.BuffId))
            {
                writer.WriteStartObject(); writer.WriteNumber("ownerId", b.OwnerId); writer.WriteNumber("targetId", b.TargetId);
                writer.WriteNumber("buffId", b.BuffId); writer.WriteString("name", b.Name); writer.WriteNumber("startTimestampMs", b.StartTimestampMs);
                writer.WriteNumber("lastRefreshTimestampMs", b.LastRefreshTimestampMs); writer.WriteNumber("endTimestampMs", b.EndTimestampMs);
                writer.WriteString("endReason", b.EndReason.ToString()); writer.WriteEndObject();
            }
            writer.WriteEndArray();
            WriteProvenance(writer, record.Provenance);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static void Write(Stream destination, EncounterRecord record)
    {
        ArgumentNullException.ThrowIfNull(destination);
        byte[] bytes = Write(record); destination.Write(bytes);
    }

    private static void WriteIdentity(Utf8JsonWriter writer, EncounterIdentity value)
    {
        writer.WriteStartObject("encounter"); writer.WriteNumber("contentId", value.ContentId); writer.WriteNumber("dungeonId", value.DungeonId);
        writer.WriteNumber("bossActorId", value.BossActorId); writer.WriteNumber("bossCode", value.BossCode); writer.WriteString("name", value.Name);
        writer.WriteNumber("lastHp", value.LastHp); writer.WriteNumber("maxHp", value.MaxHp); writer.WriteEndObject();
    }

    private static void WriteEvent(Utf8JsonWriter writer, DamageRecord e)
    {
        writer.WriteStartObject(); writer.WriteNumber("timestampMs", e.TimestampMs); writer.WriteNumber("sourceActorId", e.SourceActorId);
        writer.WriteNumber("attributedActorId", e.AttributedActorId); writer.WriteString("actorName", e.ActorName); writer.WriteNumber("targetActorId", e.TargetActorId);
        writer.WriteBoolean("isBossTarget", e.IsBossTarget); writer.WriteNumber("skillId", e.SkillId); writer.WriteString("skillName", e.SkillName);
        writer.WriteNumber("damage", e.Damage); writer.WriteNumber("multiDamage", e.MultiDamage); writer.WriteNumber("healing", e.Healing);
        writer.WriteNumber("specialMask", e.SpecialMask); writer.WriteNumber("damageType", e.DamageType); writer.WriteString("category", e.Category.ToString()); writer.WriteEndObject();
    }

    private static void WriteProvenance(Utf8JsonWriter writer, DataProvenance p)
    {
        writer.WriteStartObject("provenance"); writer.WriteString("appVersion", p.AppVersion); writer.WriteNumber("abiVersion", p.AbiVersion);
        writer.WriteNumber("dataVersion", p.DataVersion); writer.WriteNumber("schemaVersion", p.SchemaVersion); writer.WriteNumber("profileVersion", p.ProtocolProfileVersion);
        writer.WriteString("profileName", p.ProtocolProfileName); writer.WriteString("backend", p.Backend); writer.WriteString("captureId", p.CaptureId);
        writer.WriteBoolean("isComplete", p.IsComplete); writer.WriteStartArray("incompleteReasons");
        foreach (string reason in p.IncompleteReasons.Order(StringComparer.Ordinal)) writer.WriteStringValue(reason);
        writer.WriteEndArray(); writer.WriteEndObject();
    }
}
