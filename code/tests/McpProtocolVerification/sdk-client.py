"""Independent official Python MCP SDK interoperability check, against a running fish."""
import asyncio,json,sys
from mcp import ClientSession
from mcp.client.stdio import stdio_client, StdioServerParameters

async def main():
    async with stdio_client(StdioServerParameters(command=sys.argv[1])) as (receive,send):
        async with ClientSession(receive,send) as session:
            init=await session.initialize()
            assert init.server_info.name=='BigBlueFish'
            tools=await session.list_tools()
            assert len(tools.tools)==9
            settings=await session.call_tool('get_settings',{})
            assert not settings.is_error
            actual=json.loads(settings.content[0].text)
            assert actual['settings']['McpEnabled']
            actions=await session.call_tool('list_actions',{})
            assert len(json.loads(actions.content[0].text))==106
            text='主人，这是通过独立 MCP SDK 传来的原文呀~'
            said=await session.call_tool('say',{'text':text})
            assert json.loads(said.content[0].text)['text']==text
            image=await session.call_tool('capture_screen',{'max_dimension':1280})
            assert image.content[1].type=='image' and image.content[1].mime_type=='image/png'
            denied=await session.call_tool('update_settings',{'settings':{'McpEnabled':False}})
            assert denied.is_error
            await session.send_ping()
            print('Official Python MCP SDK: initialize, 9 tools, settings, 106 actions, literal speech, PNG image, protected switch and ping passed.')

asyncio.run(main())

