﻿using NAPS2.Scan;

namespace NAPS2.ImportExport.Email;

public enum EmailProviderType
{
    System,
    [LocalizedDescription(typeof(SettingsResources), "EmailProviderType_CustomSmtp")]
    CustomSmtp,
    [LocalizedDescription(typeof(SettingsResources), "EmailProviderType_Gmail")]
    Gmail,
    [LocalizedDescription(typeof(SettingsResources), "EmailProviderType_OutlookWeb")]
    OutlookWeb
}