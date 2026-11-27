using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Finex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using NSubstitute;

namespace Finex.UnitTests.Catalog;

internal static class AsyncQueryableTestHelper
{
    public static DbSet<T> CreateMockDbSet<T>(List<T> data) where T : class
    {
        IQueryable<T> queryable = new TestAsyncEnumerable<T>(data);
        var dbSet = Substitute.For<DbSet<T>, IQueryable<T>, IAsyncEnumerable<T>>();

        ((IQueryable<T>)dbSet).Provider.Returns(queryable.Provider);
        ((IQueryable<T>)dbSet).Expression.Returns(queryable.Expression);
        ((IQueryable<T>)dbSet).ElementType.Returns(queryable.ElementType);
        ((IQueryable<T>)dbSet).GetEnumerator().Returns(_ => data.GetEnumerator());
        ((IAsyncEnumerable<T>)dbSet).GetAsyncEnumerator(Arg.Any<CancellationToken>())
            .Returns(_ => new TestAsyncEnumerator<T>(data.GetEnumerator()));

        dbSet.When(d => d.Add(Arg.Any<T>())).Do(info => data.Add(info.Arg<T>()));
        dbSet.When(d => d.Remove(Arg.Any<T>())).Do(info => data.Remove(info.Arg<T>()));

        return dbSet;
    }

    public static Product CreateProductWithCategory(int categoryId)
    {
        var product = (Product)RuntimeHelpers.GetUninitializedObject(typeof(Product));
        product.CategoryId = categoryId;
        return product;
    }

    private sealed class TestAsyncQueryProvider<TEntity>(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => new TestAsyncEnumerable<TEntity>(expression);
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new TestAsyncEnumerable<TElement>(expression);
        public object? Execute(Expression expression) => inner.Execute(expression);
        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var expectedResultType = typeof(TResult).GetGenericArguments()[0];
            var executionResult = typeof(IQueryProvider)
                .GetMethod(nameof(IQueryProvider.Execute), 1, [typeof(Expression)])!
                .MakeGenericMethod(expectedResultType)
                .Invoke(this, [expression]);

            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(expectedResultType)
                .Invoke(null, [executionResult])!;
        }
    }

    private sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(IEnumerable<T> enumerable) : base(enumerable) { }
        public TestAsyncEnumerable(Expression expression) : base(expression) { }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
    }

    private sealed class TestAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;
        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());
        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}