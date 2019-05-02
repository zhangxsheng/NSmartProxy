﻿using System;
using System.Collections.Generic;

namespace NSmartProxy.Database
{
    public interface IDbOperator : IDisposable
    {
        IDbOperator Open();
        void Insert(long key, string value);
        void Update(long key, string value);
        List<string> Select(int startIndex, int length);
        void Delete(int index);
        long GetLength();
        void Close();
        bool Exist(string key);
    }
}