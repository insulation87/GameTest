\set ON_ERROR_STOP on
begin;
insert into auth.users(id,email,email_confirmed_at) values
('00000000-0000-0000-0000-000000000001','first@example.com',now()),
('00000000-0000-0000-0000-000000000002','second@example.com',now()),
('00000000-0000-0000-0000-000000000003','outsider@example.com',now());
insert into public.game_profiles(id,code,name) values
('00000000-0000-0000-0000-000000000001','AAAAAAAAAAAA','房主'),
('00000000-0000-0000-0000-000000000002','BBBBBBBBBBBB','好友'),
('00000000-0000-0000-0000-000000000003','CCCCCCCCCCCC','局外人');
insert into public.game_friendships(low_id,high_id,requested_by,accepted) values
('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000001',true);
do $$ begin
  if has_function_privilege('anon','public.game_room_invites(text,text,text,text)','execute') or
    has_table_privilege('authenticated','public.game_invite_rooms','select,insert,update,delete') or
    has_table_privilege('authenticated','public.game_room_invitations','select,insert,update,delete') then raise exception 'Direct access exposed'; end if;
end $$;
set local role authenticated;
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000001',true);
do $$ begin
  begin perform public.game_room_invites('invite','00000000-0000-0000-0000-000000000003','23456789AB','secret'); raise exception 'Nonfriend invited'; exception when invalid_parameter_value then null; end;
  begin perform public.game_room_invites('invite','00000000-0000-0000-0000-000000000001','23456789AB','secret'); raise exception 'Self invited'; exception when invalid_parameter_value then null; end;
  begin perform public.game_room_invites('invite','00000000-0000-0000-0000-000000000002','bad','secret'); raise exception 'Bad room allowed'; exception when invalid_parameter_value then null; end;
  perform public.game_room_invites('invite','00000000-0000-0000-0000-000000000002','23456789AB','secret');
  perform public.game_room_invites('invite','00000000-0000-0000-0000-000000000002','23456789AB','secret');
end $$;
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000002',true);
do $$ declare state jsonb; id text; begin
  state := public.game_room_invites('list'); id := state->'invites'->0->>'id';
  if jsonb_array_length(state->'invites') <> 1 or state::text like '%secret%' then raise exception 'Duplicate or password leak'; end if;
  perform set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000003',true);
  if jsonb_array_length(public.game_room_invites('list')->'invites') <> 0 then raise exception 'Outsider list leak'; end if;
  begin perform public.game_room_invites('accept',id); raise exception 'Outsider accepted'; exception when invalid_parameter_value then null; end;
  perform set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000002',true);
  state := public.game_room_invites('accept',id);
  if state->>'room_password' <> 'secret' or state->>'room_code' <> '23456789AB' or jsonb_array_length(state->'invites') <> 0 then raise exception 'Acceptance failed'; end if;
  begin perform public.game_room_invites('accept',id); raise exception 'Replayed invite accepted'; exception when invalid_parameter_value then null; end;
end $$;
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000001',true);
select public.game_room_invites('invite','00000000-0000-0000-0000-000000000002','23456789AB','secret');
reset role;
update public.game_room_invitations set expires_at = now() - interval '1 second';
set local role authenticated;
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000002',true);
do $$ begin
  if jsonb_array_length(public.game_room_invites('list')->'invites') <> 0 then raise exception 'Expired invite listed'; end if;
end $$;
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000001',true);
select public.game_room_invites('invite','00000000-0000-0000-0000-000000000002','23456789AB','secret');
reset role;
update public.game_invite_rooms set active_until = now() - interval '1 second';
set local role authenticated;
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000002',true);
do $$ begin
  if jsonb_array_length(public.game_room_invites('list')->'invites') <> 0 then raise exception 'Dead host invite listed'; end if;
end $$;
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000001',true);
select public.game_room_invites('heartbeat',null,'23456789AB');
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000002',true);
do $$ declare id text; begin
  id := public.game_room_invites('list')->'invites'->0->>'id';
  if id is null then raise exception 'Heartbeat failed'; end if;
  perform public.game_room_invites('decline',id);
  if jsonb_array_length(public.game_room_invites('list')->'invites') <> 0 then raise exception 'Decline failed'; end if;
end $$;
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000001',true);
select public.game_room_invites('invite','00000000-0000-0000-0000-000000000002','23456789AB','secret');
select public.game_social('remove','00000000-0000-0000-0000-000000000002');
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000002',true);
do $$ begin
  if jsonb_array_length(public.game_room_invites('list')->'invites') <> 0 then raise exception 'Removed friend invite listed'; end if;
end $$;
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000001',true);
select public.game_room_invites('close',null,'23456789AB');
select set_config('request.jwt.claim.sub','',true);
do $$ begin
  begin perform public.game_room_invites('list'); raise exception 'Unsigned request accepted'; exception when insufficient_privilege then null; end;
end $$;
rollback;
select 'PASS: room invite SQL permissions, friend consent, self/nonfriend rejection, deduplication, recipient-only credentials, replay, expiration, heartbeat, decline, removal, close and anonymous rejection';
