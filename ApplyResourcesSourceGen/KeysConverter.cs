// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ApplyResourcesSourceGen;

internal static class KeysConverter
{
    
    public static string? GetCode(string value)
    {
        var code = string.Join(" | ", value.Split(new char[] { '+' }).Select(s =>
        {
            return s.Trim() switch
            {
                "0" => "D0",
                "1" => "D1",
                "2" => "D2",
                "3" => "D3",
                "4" => "D4",
                "5" => "D5",
                "6" => "D6",
                "7" => "D7",
                "8" => "D8",
                "9" => "D9",
                "Ctrl" => "Control",
                "Del" => "Delete",
                "PgDn" => "PageDown",
                "PgUp" => "PageUp",
                string _s => _s
            };
        }).Select(s => "System.Windows.Forms.Keys." + s));

        return code;
    }


}