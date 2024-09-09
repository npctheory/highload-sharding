using Core.Application.Users.DTO;
using Core.Application.Users.Queries.GetUser;
using Core.Application.Users.Queries.SearchUsers;
using AutoMapper;
using Core.Domain.Entities;
using Core.Application.Friends.DTO;
using Core.Application.Posts.DTO;

namespace Core.Application.Mapping
{
    public class ApplicationProfile : Profile
    {
        public ApplicationProfile()
        {
            CreateMap<User, UserDTO>();
            CreateMap<Friendship, FriendDTO>();
            CreateMap<Post, PostDTO>();
        }
    }
}