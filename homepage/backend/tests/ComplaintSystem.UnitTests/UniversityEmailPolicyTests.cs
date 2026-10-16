using ComplaintSystem.Application.Security;

namespace ComplaintSystem.UnitTests;

public sealed class UniversityEmailPolicyTests
{
    [Theory]
    [InlineData("ce-sabrown0922@st.umat.edu.gh")]
    [InlineData("ma-ama.mensah0922@st.umat.edu.gh")]
    [InlineData("ds-student0124@st.umat.edu.gh")]
    public void Approved_student_email_is_accepted(string email) =>
        Assert.True(UniversityEmailPolicy.IsApprovedStudentEmail(email));

    [Theory]
    [InlineData("student@gmail.com")]
    [InlineData("officer@umat.edu.gh")]
    [InlineData("xx-student@st.umat.edu.gh")]
    public void Non_student_email_is_rejected(string email) =>
        Assert.False(UniversityEmailPolicy.IsApprovedStudentEmail(email));

    [Fact]
    public void Staff_email_requires_root_umat_domain()
    {
        Assert.True(UniversityEmailPolicy.IsApprovedStaffEmail("officer@umat.edu.gh"));
        Assert.False(UniversityEmailPolicy.IsApprovedStaffEmail("officer@st.umat.edu.gh"));
    }

    [Fact]
    public void Reference_and_index_examples_are_valid()
    {
        Assert.True(UniversityEmailPolicy.IsValidReferenceNumber("9012661222"));
    }

    [Theory]
    [InlineData("9011234522")]
    [InlineData("9010000024")]
    [InlineData("9015432126")]
    [InlineData("9019999928")]
    [InlineData(" 9011234522 ")]
    public void Valid_student_reference_numbers_are_accepted(string referenceNumber) =>
        Assert.True(UniversityEmailPolicy.IsValidReferenceNumber(referenceNumber));

    [Theory]
    [InlineData("8011234522")]
    [InlineData("901123421")]
    [InlineData("9011234521")]
    [InlineData("90112A4524")]
    [InlineData("90112345202")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_student_reference_numbers_are_rejected(string? referenceNumber) =>
        Assert.False(UniversityEmailPolicy.IsValidReferenceNumber(referenceNumber));
}
