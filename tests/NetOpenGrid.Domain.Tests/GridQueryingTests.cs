using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;
using Xunit;

namespace NetOpenGrid.Domain.Tests;

public class PageRequestTests
{
    [Theory]
    [InlineData(1, 25, 0)]
    [InlineData(2, 10, 10)]
    [InlineData(5, 50, 200)]
    public void Skip_ComputesZeroBasedOffset(int page, int pageSize, int expected) =>
        Assert.Equal(expected, new PageRequest(page, pageSize).Skip);

    [Fact]
    public void Skip_NegativePageIsClampedToFirst() =>
        Assert.Equal(0, new PageRequest(-3, 10).Skip);

    [Fact]
    public void Normalized_ClampsPageSizeToMaximum()
    {
        var normalized = new PageRequest(-1, 9_999).Normalized(maxPageSize: 100);

        Assert.Equal(1, normalized.Page);
        Assert.Equal(100, normalized.PageSize);
    }

    [Fact]
    public void Normalized_KeepsValidValues()
    {
        var normalized = new PageRequest(3, 25).Normalized(maxPageSize: 100);

        Assert.Equal(3, normalized.Page);
        Assert.Equal(25, normalized.PageSize);
    }
}

public class PageResultTests
{
    [Fact]
    public void TotalsReflectFilteringNotPaging()
    {
        var page = new PageResult<int>(Enumerable.Range(0, 5).ToArray(), TotalCount: 42, Page: 2, PageSize: 5);

        Assert.Equal(9, page.TotalPages);
        Assert.True(page.HasNext);
        Assert.True(page.HasPrevious);
    }

    [Fact]
    public void LastPageHasNoNext()
    {
        var page = new PageResult<string>([], TotalCount: 10, Page: 2, PageSize: 5);

        Assert.False(page.HasNext);
        Assert.True(page.HasPrevious);
    }
}

public class FilterOperatorMapperTests
{
    [Theory]
    [InlineData("equals", FilterOperator.Equals)]
    [InlineData("not-equals", FilterOperator.NotEquals)]
    [InlineData("contains", FilterOperator.Contains)]
    [InlineData("~", FilterOperator.Contains)]
    [InlineData("gt", FilterOperator.GreaterThan)]
    [InlineData(">=", FilterOperator.GreaterThanOrEqual)]
    [InlineData("<=", FilterOperator.LessThanOrEqual)]
    [InlineData("is-empty", FilterOperator.IsEmpty)]
    [InlineData("IS-NOT-EMPTY", FilterOperator.IsNotEmpty)]
    public void TryParse_AcceptsTokensAndSymbols(string raw, FilterOperator expected)
    {
        Assert.True(FilterOperatorMapper.TryParse(raw, out var op));
        Assert.Equal(expected, op);
    }

    [Fact]
    public void TryParse_RejectsUnknownTokens()
    {
        Assert.False(FilterOperatorMapper.TryParse("regex", out _));
        Assert.False(FilterOperatorMapper.TryParse(null, out _));
    }

    [Fact]
    public void ToSet_ToToken_RoundTrip()
    {
        foreach (var op in Enum.GetValues<FilterOperator>())
        {
            Assert.Contains(op, FilterOperatorMapper.ToOperators(FilterOperatorMapper.ToSet(op)));
        }
    }
}
