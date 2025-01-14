﻿using DBFileReaderLib.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace DBFileReaderLib.Readers
{
    class WDC3Row : IDBRow
    {
        private BaseReader m_reader;
        private readonly int m_dataOffset;
        private readonly int m_dataPosition;
        private readonly int m_recordOffset;
        private readonly int m_recordIndex;

        public int Id { get; set; }
        public BitReader Data { get; set; }

        private readonly FieldMetaData[] m_fieldMeta;
        private readonly ColumnMetaData[] m_columnMeta;
        private readonly Value32[][] m_palletData;
        private readonly Dictionary<int, Value32>[] m_commonData;
        private readonly int m_refID;

        public WDC3Row(BaseReader reader, BitReader data, int id, int refID, int recordIndex)
        {
            m_reader = reader;
            Data = data;
            m_recordOffset = (recordIndex * reader.RecordSize) - (reader.RecordsCount * reader.RecordSize);
            m_recordIndex = recordIndex;

            m_dataOffset = Data.Offset;
            m_dataPosition = Data.Position;

            m_fieldMeta = reader.Meta;
            m_columnMeta = reader.ColumnMeta;
            m_palletData = reader.PalletData;
            m_commonData = reader.CommonData;
            m_refID = refID;

            Id = id;
        }

        private static Dictionary<Type, Func<int, BitReader, int, FieldMetaData, ColumnMetaData, Value32[], Dictionary<int, Value32>, Dictionary<long, string>, BaseReader, object>> simpleReaders = new Dictionary<Type, Func<int, BitReader, int, FieldMetaData, ColumnMetaData, Value32[], Dictionary<int, Value32>, Dictionary<long, string>, BaseReader, object>>
        {
            [typeof(ulong)] = (id, data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<ulong>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(long)] = (id, data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<long>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(float)] = (id, data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<float>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(int)] = (id, data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<int>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(uint)] = (id, data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<uint>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(short)] = (id, data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<short>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(ushort)] = (id, data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<ushort>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(sbyte)] = (id, data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<sbyte>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(byte)] = (id, data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<byte>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(string)] = (id, data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => header.Flags.HasFlagExt(DB2Flags.Sparse) ? data.ReadCString() :
                stringTable[recordOffset + (data.Position >> 3) + GetFieldValue<int>(id, data, fieldMeta, columnMeta, palletData, commonData)],
        };

        private static Dictionary<Type, Func<BitReader, int, FieldMetaData, ColumnMetaData, Value32[], Dictionary<int, Value32>, Dictionary<long, string>, object>> arrayReaders = new Dictionary<Type, Func<BitReader, int, FieldMetaData, ColumnMetaData, Value32[], Dictionary<int, Value32>, Dictionary<long, string>, object>>
        {
            [typeof(ulong[])] = (data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable) => GetFieldValueArray<ulong>(data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(long[])] = (data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable) => GetFieldValueArray<long>(data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(float[])] = (data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable) => GetFieldValueArray<float>(data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(int[])] = (data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable) => GetFieldValueArray<int>(data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(uint[])] = (data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable) => GetFieldValueArray<uint>(data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(ulong[])] = (data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable) => GetFieldValueArray<ulong>(data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(short[])] = (data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable) => GetFieldValueArray<short>(data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(ushort[])] = (data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable) => GetFieldValueArray<ushort>(data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(byte[])] = (data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable) => GetFieldValueArray<byte>(data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(sbyte[])] = (data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable) => GetFieldValueArray<sbyte>(data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(string[])] = (data, recordOffset, fieldMeta, columnMeta, palletData, commonData, stringTable) => GetFieldValueStringArray(data, fieldMeta, columnMeta, recordOffset, stringTable),
        };

        public void GetFields<T>(FieldCache<T>[] fields, T entry)
        {
            int indexFieldOffSet = 0;

            Data.Position = m_dataPosition;
            Data.Offset = m_dataOffset;

            for (int i = 0; i < fields.Length; i++)
            {
                FieldCache<T> info = fields[i];
                if (i == m_reader.IdFieldIndex)
                {
                    if (Id != -1)
                        indexFieldOffSet++;
                    else
                        Id = GetFieldValue<int>(0, Data, m_fieldMeta[i], m_columnMeta[i], m_palletData[i], m_commonData[i]);

                    info.Setter(entry, Convert.ChangeType(Id, info.Field.FieldType));
                    continue;
                }

                object value = null;
                int fieldIndex = i - indexFieldOffSet;

                if (fieldIndex >= m_reader.Meta.Length)
                {
                    info.Setter(entry, Convert.ChangeType(m_refID, info.Field.FieldType));
                    continue;
                }

                if (info.IsArray)
                {
                    if (arrayReaders.TryGetValue(info.Field.FieldType, out var reader))
                        value = reader(Data, m_recordOffset, m_fieldMeta[fieldIndex], m_columnMeta[fieldIndex], m_palletData[fieldIndex], m_commonData[fieldIndex], m_reader.StringTable);
                    else
                        throw new Exception("Unhandled array type: " + typeof(T).Name);
                }
                else
                {
                    if (simpleReaders.TryGetValue(info.Field.FieldType, out var reader))
                        value = reader(Id, Data, m_recordOffset, m_fieldMeta[fieldIndex], m_columnMeta[fieldIndex], m_palletData[fieldIndex], m_commonData[fieldIndex], m_reader.StringTable, m_reader);
                    else
                        throw new Exception("Unhandled field type: " + typeof(T).Name);
                }

                info.Setter(entry, value);
            }
        }

        private static T GetFieldValue<T>(int Id, BitReader r, FieldMetaData fieldMeta, ColumnMetaData columnMeta, Value32[] palletData, Dictionary<int, Value32> commonData) where T : struct
        {
            switch (columnMeta.CompressionType)
            {
                case CompressionType.None:
                    {
                        int bitSize = 32 - fieldMeta.Bits;
                        if (bitSize <= 0)
                            bitSize = columnMeta.Immediate.BitWidth;

                        return r.ReadValue64(bitSize).GetValue<T>();
                    }
                case CompressionType.SignedImmediate:
                    {
                        return r.ReadValue64Signed(columnMeta.Immediate.BitWidth).GetValue<T>();
                    }
                case CompressionType.Immediate:
                    {
                        return r.ReadValue64(columnMeta.Immediate.BitWidth).GetValue<T>();
                    }
                case CompressionType.Common:
                    {
                        if (commonData.TryGetValue(Id, out Value32 val))
                            return val.GetValue<T>();
                        return columnMeta.Common.DefaultValue.GetValue<T>();
                    }
                case CompressionType.Pallet:
                    {
                        uint palletIndex = r.ReadUInt32(columnMeta.Pallet.BitWidth);
                        return palletData[palletIndex].GetValue<T>();
                    }
                case CompressionType.PalletArray:
                    {
                        if (columnMeta.Pallet.Cardinality != 1)
                            break;

                        uint palletArrayIndex = r.ReadUInt32(columnMeta.Pallet.BitWidth);
                        return palletData[(int)palletArrayIndex].GetValue<T>();
                    }
            }

            throw new Exception(string.Format("Unexpected compression type {0}", columnMeta.CompressionType));
        }

        private static T[] GetFieldValueArray<T>(BitReader r, FieldMetaData fieldMeta, ColumnMetaData columnMeta, Value32[] palletData, Dictionary<int, Value32> commonData) where T : struct
        {
            T[] array;
            switch (columnMeta.CompressionType)
            {
                case CompressionType.None:
                    {
                        int bitSize = 32 - fieldMeta.Bits;
                        if (bitSize <= 0)
                            bitSize = columnMeta.Immediate.BitWidth;

                        array = new T[columnMeta.Size / (Unsafe.SizeOf<T>() * 8)];
                        for (int i = 0; i < array.Length; i++)
                            array[i] = r.ReadValue64(bitSize).GetValue<T>();

                        return array;
                    }
                case CompressionType.PalletArray:
                    {
                        int cardinality = columnMeta.Pallet.Cardinality;
                        uint palletArrayIndex = r.ReadUInt32(columnMeta.Pallet.BitWidth);

                        array = new T[cardinality];
                        for (int i = 0; i < array.Length; i++)
                            array[i] = palletData[i + cardinality * (int)palletArrayIndex].GetValue<T>();

                        return array;
                    }
            }

            throw new Exception(string.Format("Unexpected compression type {0}", columnMeta.CompressionType));
        }

        private static string[] GetFieldValueStringArray(BitReader r, FieldMetaData fieldMeta, ColumnMetaData columnMeta, int recordOffset, Dictionary<long, string> stringTable)
        {
            switch (columnMeta.CompressionType)
            {
                case CompressionType.None:
                    int bitSize = 32 - fieldMeta.Bits;
                    if (bitSize <= 0)
                        bitSize = columnMeta.Immediate.BitWidth;

                    string[] array = new string[columnMeta.Size / (sizeof(int) * 8)];
                    for (int i = 0; i < array.Length; i++)
                        array[i] = stringTable[(r.Position >> 3) + recordOffset + r.ReadValue64(bitSize).GetValue<int>()];

                    return array;
            }

            throw new Exception(string.Format("Unexpected compression type {0}", columnMeta.CompressionType));
        }

        public IDBRow Clone()
        {
            return (IDBRow)MemberwiseClone();
        }
    }

    class WDC3Reader : BaseEncryptionSupportingReader
    {
        private const int HeaderSize = 72;
        private const uint WDC3FmtSig = 0x33434457; // WDC3

        public WDC3Reader(string dbcFile) : this(new FileStream(dbcFile, FileMode.Open)) { }

        public WDC3Reader(Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.BaseStream.Length < HeaderSize)
                    throw new InvalidDataException("WDC3 file is corrupted!");

                uint magic = reader.ReadUInt32();

                if (magic != WDC3FmtSig)
                    throw new InvalidDataException("WDC3 file is corrupted!");

                RecordsCount            = reader.ReadInt32();
                FieldsCount             = reader.ReadInt32();
                RecordSize              = reader.ReadInt32();
                StringTableSize         = reader.ReadInt32();
                TableHash               = reader.ReadUInt32();
                LayoutHash              = reader.ReadUInt32();
                MinIndex                = reader.ReadInt32();
                MaxIndex                = reader.ReadInt32();
                Locale                  = reader.ReadInt32();
                Flags                   = (DB2Flags)reader.ReadUInt16();
                IdFieldIndex            = reader.ReadUInt16();
                int totalFieldsCount    = reader.ReadInt32();
                PackedDataOffset        = reader.ReadInt32();   // Offset within the field where packed data starts
                LookupColumnCount       = reader.ReadInt32();   // count of lookup columns
                int columnMetaDataSize  = reader.ReadInt32();   // 24 * NumFields bytes, describes column bit packing, {ushort recordOffset, ushort size, uint additionalDataSize, uint compressionType, uint packedDataOffset or commonvalue, uint cellSize, uint cardinality}[NumFields], sizeof(DBC2CommonValue) == 8
                int commonDataSize      = reader.ReadInt32();
                int palletDataSize      = reader.ReadInt32();   // in bytes, sizeof(DBC2PalletValue) == 4
                int sectionsCount       = reader.ReadInt32();

                if (sectionsCount == 0 || RecordsCount == 0)
                    return;

                var sections = reader.ReadArray<SectionHeaderWDC3>(sectionsCount).ToList();
                m_sections = sections.OfType<IEncryptableDatabaseSection>().ToList();

                // field meta data
                Meta = reader.ReadArray<FieldMetaData>(FieldsCount);

                // column meta data
                ColumnMeta = reader.ReadArray<ColumnMetaData>(FieldsCount);

                // pallet data
                PalletData = new Value32[ColumnMeta.Length][];
                for (int i = 0; i < ColumnMeta.Length; i++)
                {
                    if (ColumnMeta[i].CompressionType == CompressionType.Pallet || ColumnMeta[i].CompressionType == CompressionType.PalletArray)
                    {
                        PalletData[i] = reader.ReadArray<Value32>((int)ColumnMeta[i].AdditionalDataSize / 4);
                    }
                }

                // common data
                CommonData = new Dictionary<int, Value32>[ColumnMeta.Length];
                for (int i = 0; i < ColumnMeta.Length; i++)
                {
                    if (ColumnMeta[i].CompressionType == CompressionType.Common)
                    {
                        var commonValues = new Dictionary<int, Value32>((int)ColumnMeta[i].AdditionalDataSize / 8);
                        CommonData[i] = commonValues;

                        for (int j = 0; j < ColumnMeta[i].AdditionalDataSize / 8; j++)
                            commonValues[reader.ReadInt32()] = reader.Read<Value32>();
                    }
                }

                int previousStringTableSize = 0, previousRecordCount = 0;
                foreach (var section in sections)
                {
                    reader.BaseStream.Position = section.FileOffset;

                    if (!Flags.HasFlagExt(DB2Flags.Sparse))
                    {
                        // records data
                        RecordsData = reader.ReadBytes(section.NumRecords * RecordSize);

                        Array.Resize(ref RecordsData, RecordsData.Length + 8); // pad with extra zeros so we don't crash when reading

                        // string data
                        if (StringTable == null)
                            StringTable = new Dictionary<long, string>(section.StringTableSize / 0x20);

                        for (int i = 0; i < section.StringTableSize;)
                        {
                            long oldPos = reader.BaseStream.Position;
                            StringTable[i + previousStringTableSize] = reader.ReadCString();
                            i += (int)(reader.BaseStream.Position - oldPos);
                        }

                        previousStringTableSize += section.StringTableSize;
                    }
                    else
                    {
                        // sparse data with inlined strings
                        RecordsData = reader.ReadBytes(section.OffsetRecordsEndOffset - section.FileOffset);

                        if (reader.BaseStream.Position != section.OffsetRecordsEndOffset)
                            throw new Exception("reader.BaseStream.Position != section.OffsetRecordsEndOffset");
                    }

                    // skip encrypted sections => has tact key + record data is zero filled
                    if (section.TactKeyLookup != 0 && Array.TrueForAll(RecordsData, x => x == 0))
                    {
                        previousRecordCount += section.NumRecords;
                        continue;
                    }

                    // index data
                    IndexData = reader.ReadArray<int>(section.IndexDataSize / 4);

                    // fix zero-filled index data
                    if (IndexData.Length > 0 && IndexData.All(x => x == 0))
                        IndexData = Enumerable.Range(MinIndex + previousRecordCount, section.NumRecords).ToArray();

                    // duplicate rows data
                    if (section.CopyTableCount > 0)
                    {
                        if (CopyData == null)
                            CopyData = new Dictionary<int, int>();

                        for (int i = 0; i < section.CopyTableCount; i++)
                            CopyData[reader.ReadInt32()] = reader.ReadInt32();
                    }

                    if (section.OffsetMapIDCount > 0)
                    {
                        // HACK unittestsparse is malformed and has sparseIndexData first
                        if (TableHash == 145293629)
                            reader.BaseStream.Position += 4 * section.OffsetMapIDCount;

                        SparseEntries = reader.ReadArray<SparseEntry>(section.OffsetMapIDCount).ToList();
                    }

                    // reference data here
                    ReferenceData refData = new ReferenceData();
                    if (section.ParentLookupDataSize > 0)
                    {
                        refData.NumRecords = reader.ReadInt32();
                        refData.MinId = reader.ReadInt32();
                        refData.MaxId = reader.ReadInt32();

                        var entries = reader.ReadArray<ReferenceEntry>(refData.NumRecords);
                        for (int i = 0; i < entries.Length; i++)
                            refData.Entries[entries[i].Index] = entries[i].Id;
                    }

                    if (section.OffsetMapIDCount > 0)
                    {
                        int[] sparseIndexData = reader.ReadArray<int>(section.OffsetMapIDCount);

                        if (section.IndexDataSize > 0 && IndexData.Length != sparseIndexData.Length)
                            throw new Exception("m_indexData.Length != sparseIndexData.Length");

                        IndexData = sparseIndexData;
                    }

                    int position = 0;
                    for (int i = 0; i < section.NumRecords; i++)
                    {
                        BitReader bitReader = new BitReader(RecordsData) { Position = 0 };

                        if (Flags.HasFlagExt(DB2Flags.Sparse))
                        {
                            bitReader.Position = position;
                            position += SparseEntries[i].Size * 8;
                        }
                        else
                        {
                            bitReader.Offset = i * RecordSize;
                        }

                        refData.Entries.TryGetValue(i, out int refId);

                        IDBRow rec = new WDC3Row(this, bitReader, section.IndexDataSize != 0 ? IndexData[i] : -1, refId, i + previousRecordCount);
                        _Records.Add(_Records.Count, rec);
                    }

                    previousRecordCount += section.NumRecords;
                }
            }
        }
    }
}
