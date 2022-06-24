﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Remore.Server.EF;

#nullable disable

namespace Remore.Server.Migrations
{
    [DbContext(typeof(ServerDbContext))]
    partial class ServerDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "6.0.5");

            modelBuilder.Entity("Remore.Library.Models.ChannelMessage", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("TEXT");

                    b.Property<string>("ChannelId")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("TEXT");

                    b.Property<string>("Message")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Username")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("ChannelId");

                    b.ToTable("ChannelMessages");
                });

            modelBuilder.Entity("Remore.Server.Channel", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("TEXT");

                    b.Property<int>("Bitrate")
                        .HasColumnType("INTEGER");

                    b.Property<byte>("ChannelType")
                        .HasColumnType("INTEGER");

                    b.Property<int>("MaxClients")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Order")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("Channels");
                });

            modelBuilder.Entity("Remore.Server.Models.PrivilegeKey", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("TEXT");

                    b.Property<string>("Key")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("PrivilegeKey");
                });

            modelBuilder.Entity("Remore.Server.Services.ServerConfiguration", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("TEXT");

                    b.Property<int>("MaxClients")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("PrivilegeKeyId")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("PrivilegeKeyId");

                    b.ToTable("Configuration");
                });

            modelBuilder.Entity("Remore.Library.Models.ChannelMessage", b =>
                {
                    b.HasOne("Remore.Server.Channel", null)
                        .WithMany("Messages")
                        .HasForeignKey("ChannelId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Remore.Server.Services.ServerConfiguration", b =>
                {
                    b.HasOne("Remore.Server.Models.PrivilegeKey", "PrivilegeKey")
                        .WithMany()
                        .HasForeignKey("PrivilegeKeyId");

                    b.Navigation("PrivilegeKey");
                });

            modelBuilder.Entity("Remore.Server.Channel", b =>
                {
                    b.Navigation("Messages");
                });
#pragma warning restore 612, 618
        }
    }
}