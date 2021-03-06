using System;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using NetCoreKit.Domain;
using NetCoreKit.Infrastructure.EfCore.Extensions;
using NetCoreKit.Utils.Extensions;
using VND.CoolStore.Services.Cart.Domain;
using VND.CoolStore.Services.Cart.Extensions;
using VND.CoolStore.Services.Cart.v1.Extensions;
using VND.CoolStore.Services.Cart.v1.Grpc;

namespace VND.CoolStore.Services.Cart.v1.Services
{
    public class CartServiceImpl : CartService.CartServiceBase
    {
        private readonly IQueryRepositoryFactory _queryFactory;
        private readonly IUnitOfWorkAsync _commandFactory;

        private readonly ICatalogGateway _catalogGateway;
        private readonly IShippingGateway _shippingGateway;
        private readonly IPromoGateway _promoGateway;

        public CartServiceImpl(IServiceProvider resolver)
        {
            _queryFactory = resolver.GetService<IQueryRepositoryFactory>();
            _commandFactory = resolver.GetService<IUnitOfWorkAsync>();

            _catalogGateway = resolver.GetService<ICatalogGateway>();
            _shippingGateway = resolver.GetService<IShippingGateway>();
            _promoGateway = resolver.GetService<IPromoGateway>();
        }

        public override async Task<GetCartResponse> GetCart(GetCartRequest request, ServerCallContext context)
        {
            var cartQuery = _queryFactory.QueryEfRepository<Domain.Cart>();

            var cart = await cartQuery.GetFullCartAsync(request.CartId.ConvertTo<Guid>(), false)
                .ToObservable()
                .SelectMany(async c =>
                    await c.CalculateCartAsync(TaxType.NoTax, _catalogGateway, _promoGateway, _shippingGateway));
            return new GetCartResponse {Result = cart.ToDto()};
        }

        public override async Task<InsertItemToNewCartResponse> InsertItemToNewCart(InsertItemToNewCartRequest request,
            ServerCallContext context)
        {
            var cartCommander = _commandFactory.RepositoryAsync<Domain.Cart>();

            var cart = await Domain.Cart.Load()
                .InsertItemToCart(request.ProductId.ConvertTo<Guid>(), request.Quantity)
                .CalculateCartAsync(
                    TaxType.NoTax,
                    _catalogGateway,
                    _promoGateway,
                    _shippingGateway);

            await cartCommander.AddAsync(cart);

            return new InsertItemToNewCartResponse {Result = cart.ToDto()};
        }

        public override async Task<UpdateItemInCartResponse> UpdateItemInCart(UpdateItemInCartRequest request,
            ServerCallContext context)
        {
            var cartCommander = _commandFactory.RepositoryAsync<Domain.Cart>();
            var cartQuery = _queryFactory.QueryEfRepository<Domain.Cart>();

            var cart = await cartQuery.GetFullCartAsync(request.CartId.ConvertTo<Guid>());
            var cartItem = cart.FindCartItem(request.ProductId.ConvertTo<Guid>());

            // if not exists then it should be a new item
            if (cartItem == null)
            {
                cart.InsertItemToCart(request.ProductId.ConvertTo<Guid>(), request.Quantity);
            }
            else
            {
                // otherwise is updating the current item in the cart
                cart.AccumulateCartItemQuantity(cartItem.Id, request.Quantity);
            }

            await cart.CalculateCartAsync(TaxType.NoTax, _catalogGateway, _promoGateway, _shippingGateway);
            await cartCommander.UpdateAsync(cart);

            return new UpdateItemInCartResponse {Result = cart.ToDto()};
        }

        public override async Task<CheckoutResponse> Checkout(CheckoutRequest request, ServerCallContext context)
        {
            var cartCommander = _commandFactory.RepositoryAsync<Domain.Cart>();
            var cartQuery = _queryFactory.QueryEfRepository<Domain.Cart>();

            var cart = await cartQuery.GetFullCartAsync(request.CartId.ConvertTo<Guid>());
            var checkoutCart = await cartCommander.UpdateAsync(cart.Checkout());

            return new CheckoutResponse
            {
                IsSucceed = checkoutCart != null
            };
        }

        public override async Task<DeleteItemResponse> DeleteItem(DeleteItemRequest request, ServerCallContext context)
        {
            var cartCommander = _commandFactory.RepositoryAsync<Domain.Cart>();
            var cartQuery = _queryFactory.QueryEfRepository<Domain.Cart>();

            var cart = await cartQuery.GetFullCartAsync(request.CartId.ConvertTo<Guid>());
            var cartItem = cart.FindCartItem(request.ProductId.ConvertTo<Guid>());

            cart.RemoveCartItem(cartItem.Id);
            await cart.CalculateCartAsync(TaxType.NoTax, _catalogGateway, _promoGateway, _shippingGateway);
            await cartCommander.UpdateAsync(cart);

            return new DeleteItemResponse {ProductId = cartItem.Product.ProductId.ToString()};
        }
    }
}
