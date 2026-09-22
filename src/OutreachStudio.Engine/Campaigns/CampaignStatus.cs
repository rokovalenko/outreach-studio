namespace OutreachStudio.Engine.Campaigns;

public enum CampaignStatus { Draft, InReview, Approved, Scheduled, Sending, Done, Failed }

[Flags]
public enum Channels { None = 0, Push = 1, Email = 2, Both = Push | Email }
