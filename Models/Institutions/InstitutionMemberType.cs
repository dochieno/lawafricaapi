namespace LawAfrica.API.Models.Institutions
{
    /// <summary>
    /// Institution member classification.
    /// - Student: must provide student reference number
    /// - Staff: includes lecturers/employees (corporate employee style)
    /// - InstitutionAdmin: manages members and seats
    /// </summary>
    public enum InstitutionMemberType
    {
        Student = 1,
        Staff = 2,
        InstitutionAdmin = 3
    }
}
