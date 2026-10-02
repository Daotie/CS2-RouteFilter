using System;
using System.Collections.Generic;

namespace RouteFilter.Persistence;

/// <summary>Byte-array implementations of the codec's sink and source, with exact bookkeeping.</summary>
public sealed class RestrictionByteSink : IRestrictionSaveSink
{
    private readonly List<byte> m_Bytes = new();

    public int Length => m_Bytes.Count;

    public byte[] ToArray() => m_Bytes.ToArray();

    public void WriteUInt(uint value) => Write(BitConverter.GetBytes(value));
    public void WriteUShort(ushort value) => Write(BitConverter.GetBytes(value));
    public void WriteInt(int value) => Write(BitConverter.GetBytes(value));
    public void WriteFloat(float value) => Write(BitConverter.GetBytes(value));
    public void WriteByte(byte value) => m_Bytes.Add(value);

    public void WriteString(string value)
    {
        WriteInt(value.Length);
        foreach (var c in value) Write(BitConverter.GetBytes(c));
    }

    public void WriteBytes(byte[] value)
    {
        if (value != null) m_Bytes.AddRange(value);
    }

    private void Write(byte[] chunk) => m_Bytes.AddRange(chunk);
}

public sealed class RestrictionByteSource : IRestrictionSaveSource
{
    private readonly byte[] m_Bytes;
    private int m_Position;
    private readonly int m_Total;

    public RestrictionByteSource(byte[] bytes, int total = -1)
    {
        m_Bytes = bytes;
        m_Total = total < 0 ? bytes.Length : total;
    }

    public long Remaining => m_Total - m_Position;

    private bool Take(int count)
    {
        if (Remaining < count || m_Position + count > m_Bytes.Length) return false;
        m_Position += count;
        return true;
    }

    public bool ReadUInt(out uint value)
    {
        value = 0;
        if (!Take(4)) return false;
        value = BitConverter.ToUInt32(m_Bytes, m_Position - 4);
        return true;
    }

    public bool ReadUShort(out ushort value)
    {
        value = 0;
        if (!Take(2)) return false;
        value = BitConverter.ToUInt16(m_Bytes, m_Position - 2);
        return true;
    }

    public bool ReadInt(out int value)
    {
        value = 0;
        if (!Take(4)) return false;
        value = BitConverter.ToInt32(m_Bytes, m_Position - 4);
        return true;
    }

    public bool ReadString(out string value)
    {
        value = null;
        if (!ReadInt(out var length)) return false;
        if (length < 0 || length > RouteFilterSaveData.MaxPrefabNameLength || !Take(length * 2)) return false;
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = BitConverter.ToChar(m_Bytes, m_Position - length * 2 + i * 2);
        value = new string(chars);
        return true;
    }

    public bool SkipToEnd()
    {
        if (Remaining < 0) return false;
        m_Position = m_Total;
        return true;
    }

    public bool ReadRemainingBytes(out byte[] value)
    {
        value = null;
        if (Remaining <= 0) return false;
        value = new byte[Remaining];
        Array.Copy(m_Bytes, m_Position, value, 0, (int)Remaining);
        m_Position = m_Total;
        return true;
    }
}
