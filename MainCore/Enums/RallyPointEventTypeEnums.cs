namespace MainCore.Enums
{
    // Values match the "eventType" radio button values on the Rally Point "Send troops" form,
    // verified from a real page capture (see RallyPointSendTroopsParser.GetEventTypeRadio).
    public enum RallyPointEventTypeEnums
    {
        AttackNormal = 3,
        AttackRaid = 4,
        Reinforcement = 5,
    }
}
